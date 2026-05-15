using Autorecord.Core.Audio;
using Autorecord.Core.Calendar;
using Autorecord.Core.Settings;
using Autorecord.Core.Utilities;

namespace Autorecord.Core.Recording;

public sealed class RecordingCoordinator
{
    private const float SilenceThreshold = 0.01f;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<IAudioRecorder> _recorderFactory;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _policyGate = new();
    private readonly object _pendingSavesGate = new();
    private readonly Dictionary<string, RecordingSession> _pendingSaves = new(StringComparer.OrdinalIgnoreCase);
    private IAudioRecorder? _recorder;
    private StopConfirmationPolicy? _stopPolicy;
    private bool _keepRecorderReady;

    public RecordingCoordinator(Func<IAudioRecorder> recorderFactory, Func<DateTimeOffset> clock)
    {
        _recorderFactory = recorderFactory;
        _clock = clock;
    }

    public event EventHandler<RecordingSession>? RecordingStarted;
    public event EventHandler<RecordingSession>? StopPromptRequired;
    public event EventHandler<RecordingSession>? RecordingSaved;
    public event EventHandler<RecordingSaveFailedEventArgs>? RecordingSaveFailed;

    public RecordingSession? CurrentSession { get; private set; }
    public bool IsRecording => CurrentSession is not null;

    public async Task ApplySettingsAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            _keepRecorderReady = settings.KeepMicrophoneReady;
            if (_keepRecorderReady)
            {
                _recorder ??= _recorderFactory();
                await _recorder.PrepareAsync(cancellationToken);
                return;
            }

            if (!IsRecording && _recorder is not null)
            {
                await _recorder.DisposeAsync();
                _recorder = null;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StartAsync(CalendarEvent calendarEvent, AppSettings settings, CancellationToken cancellationToken)
    {
        RecordingSession? sessionToRaise = null;

        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (IsRecording)
            {
                return;
            }

            var startedAt = _clock();
            var outputPath = RecordingFileNamer.GetAvailablePath(settings.OutputFolder, startedAt);
            _keepRecorderReady = settings.KeepMicrophoneReady;
            var recorder = _recorder ?? _recorderFactory();
            var session = new RecordingSession(calendarEvent, startedAt, outputPath);
            lock (_policyGate)
            {
                _stopPolicy = settings.AutoStopRecordingOnSilence
                    ? new StopConfirmationPolicy(
                        TimeSpan.FromMinutes(settings.SilencePromptMinutes),
                        TimeSpan.FromMinutes(settings.RetryPromptMinutes))
                    : null;
            }

            _recorder = recorder;
            recorder.FileSaved -= OnFileSaved;
            recorder.FileSaveFailed -= OnFileSaveFailed;
            recorder.FileSaved += OnFileSaved;
            recorder.FileSaveFailed += OnFileSaveFailed;
            recorder.LevelChanged += OnLevelChanged;
            CurrentSession = session;

            try
            {
                if (_keepRecorderReady)
                {
                    await recorder.PrepareAsync(cancellationToken);
                }

                await recorder.StartAsync(outputPath, cancellationToken);
            }
            catch
            {
                recorder.LevelChanged -= OnLevelChanged;
                recorder.FileSaved -= OnFileSaved;
                recorder.FileSaveFailed -= OnFileSaveFailed;
                CurrentSession = null;
                _recorder = null;
                lock (_policyGate)
                {
                    _stopPolicy = null;
                }

                await recorder.DisposeAsync();
                throw;
            }

            sessionToRaise = session;
        }
        finally
        {
            _lifecycleGate.Release();
        }

        if (sessionToRaise is not null)
        {
            RecordingStarted?.Invoke(this, sessionToRaise);
        }
    }

    public async Task ConfirmStopAsync(CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (_recorder is null || CurrentSession is null)
            {
                return;
            }

            var recorder = _recorder;
            var session = CurrentSession;

            if (!_keepRecorderReady)
            {
                _recorder = null;
            }

            CurrentSession = null;
            recorder.LevelChanged -= OnLevelChanged;
            lock (_pendingSavesGate)
            {
                _pendingSaves[session.OutputPath] = session;
            }

            lock (_policyGate)
            {
                _stopPolicy = null;
            }

            try
            {
                await recorder.StopAsync(cancellationToken);
            }
            catch
            {
                lock (_pendingSavesGate)
                {
                    _pendingSaves.Remove(session.OutputPath);
                }

                throw;
            }
            finally
            {
                if (!_keepRecorderReady)
                {
                    await recorder.DisposeAsync();
                }
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public void DeclineStop()
    {
        lock (_policyGate)
        {
            _stopPolicy?.RecordNo(_clock());
        }
    }

    public void IgnoreStopPrompt()
    {
        lock (_policyGate)
        {
            _stopPolicy?.RecordNoAnswer(_clock());
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycleGate.WaitAsync();
        try
        {
            if (_recorder is not null)
            {
                _recorder.FileSaved -= OnFileSaved;
                _recorder.FileSaveFailed -= OnFileSaveFailed;
                await _recorder.DisposeAsync();
                _recorder = null;
            }

            CurrentSession = null;
            lock (_pendingSavesGate)
            {
                _pendingSaves.Clear();
            }
            lock (_policyGate)
            {
                _stopPolicy = null;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private void OnLevelChanged(object? sender, AudioLevel level)
    {
        var session = CurrentSession;
        var policy = _stopPolicy;

        if (session is null || policy is null)
        {
            return;
        }

        var shouldPrompt = false;
        lock (_policyGate)
        {
            shouldPrompt = ReferenceEquals(policy, _stopPolicy)
                && policy.ShouldPrompt(_clock(), level.BothSilent(SilenceThreshold));
        }

        if (shouldPrompt)
        {
            StopPromptRequired?.Invoke(this, session);
        }
    }

    private void OnFileSaved(object? sender, AudioFileSavedEventArgs args)
    {
        RecordingSession? session;
        lock (_pendingSavesGate)
        {
            if (!_pendingSaves.Remove(args.RequestedOutputPath, out session))
            {
                return;
            }
        }

        var savedSession = string.Equals(session.OutputPath, args.SavedOutputPath, StringComparison.OrdinalIgnoreCase)
            ? session
            : session with { OutputPath = args.SavedOutputPath };
        RecordingSaved?.Invoke(this, savedSession);
    }

    private void OnFileSaveFailed(object? sender, AudioFileSaveFailedEventArgs args)
    {
        RecordingSession? session;
        lock (_pendingSavesGate)
        {
            if (!_pendingSaves.Remove(args.RequestedOutputPath, out session))
            {
                return;
            }
        }

        RecordingSaveFailed?.Invoke(this, new RecordingSaveFailedEventArgs(session, args.TemporaryWavPath, args.Error));
    }
}
