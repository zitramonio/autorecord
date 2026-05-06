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
    private IAudioRecorder? _recorder;
    private StopConfirmationPolicy? _stopPolicy;

    public RecordingCoordinator(Func<IAudioRecorder> recorderFactory, Func<DateTimeOffset> clock)
    {
        _recorderFactory = recorderFactory;
        _clock = clock;
    }

    public event EventHandler<RecordingSession>? RecordingStarted;
    public event EventHandler<RecordingSession>? StopPromptRequired;
    public event EventHandler<RecordingSession>? RecordingSaved;

    public RecordingSession? CurrentSession { get; private set; }
    public bool IsRecording => CurrentSession is not null;

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
            var recorder = _recorderFactory();
            var session = new RecordingSession(calendarEvent, startedAt, outputPath);
            lock (_policyGate)
            {
                _stopPolicy = new StopConfirmationPolicy(
                    TimeSpan.FromMinutes(settings.SilencePromptMinutes),
                    TimeSpan.FromMinutes(settings.RetryPromptMinutes));
            }

            _recorder = recorder;
            recorder.LevelChanged += OnLevelChanged;
            CurrentSession = session;

            try
            {
                await recorder.StartAsync(outputPath, cancellationToken);
            }
            catch
            {
                recorder.LevelChanged -= OnLevelChanged;
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
        RecordingSession? sessionToRaise = null;

        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (_recorder is null || CurrentSession is null)
            {
                return;
            }

            var recorder = _recorder;
            var session = CurrentSession;

            _recorder = null;
            CurrentSession = null;
            recorder.LevelChanged -= OnLevelChanged;
            lock (_policyGate)
            {
                _stopPolicy = null;
            }

            try
            {
                await recorder.StopAsync(cancellationToken);
                sessionToRaise = session;
            }
            finally
            {
                await recorder.DisposeAsync();
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }

        if (sessionToRaise is not null)
        {
            RecordingSaved?.Invoke(this, sessionToRaise);
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
}
