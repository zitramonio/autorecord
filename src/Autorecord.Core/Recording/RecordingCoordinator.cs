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
        if (IsRecording)
        {
            return;
        }

        var startedAt = _clock();
        var outputPath = RecordingFileNamer.GetAvailablePath(settings.OutputFolder, startedAt);
        _stopPolicy = new StopConfirmationPolicy(
            TimeSpan.FromMinutes(settings.SilencePromptMinutes),
            TimeSpan.FromMinutes(settings.RetryPromptMinutes));
        _recorder = _recorderFactory();
        _recorder.LevelChanged += OnLevelChanged;
        CurrentSession = new RecordingSession(calendarEvent, startedAt, outputPath);

        await _recorder.StartAsync(outputPath, cancellationToken);

        RecordingStarted?.Invoke(this, CurrentSession);
    }

    public async Task ConfirmStopAsync(CancellationToken cancellationToken)
    {
        if (_recorder is null || CurrentSession is null)
        {
            return;
        }

        var recorder = _recorder;
        var session = CurrentSession;
        _recorder = null;
        _stopPolicy = null;
        CurrentSession = null;
        recorder.LevelChanged -= OnLevelChanged;

        await recorder.StopAsync(cancellationToken);
        await recorder.DisposeAsync();

        RecordingSaved?.Invoke(this, session);
    }

    public void DeclineStop() => _stopPolicy?.RecordNo(_clock());

    public void IgnoreStopPrompt() => _stopPolicy?.RecordNoAnswer(_clock());

    private void OnLevelChanged(object? sender, AudioLevel level)
    {
        if (CurrentSession is null || _stopPolicy is null)
        {
            return;
        }

        if (_stopPolicy.ShouldPrompt(_clock(), level.BothSilent(SilenceThreshold)))
        {
            StopPromptRequired?.Invoke(this, CurrentSession);
        }
    }
}
