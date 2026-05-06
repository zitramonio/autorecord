using Autorecord.Core.Audio;
using Autorecord.Core.Calendar;
using Autorecord.Core.Recording;
using Autorecord.Core.Settings;

namespace Autorecord.Core.Tests;

public sealed class RecordingCoordinatorTests
{
    [Fact]
    public async Task StartCreatesSessionAndStartsRecorder()
    {
        var now = new DateTimeOffset(2026, 5, 6, 18, 42, 0, TimeSpan.Zero);
        using var temp = new TempFolder();
        var recorder = new FakeAudioRecorder();
        var coordinator = new RecordingCoordinator(() => recorder, () => now);
        var calendarEvent = CreateEvent(now);
        var settings = CreateSettings(temp.Path);
        RecordingSession? started = null;

        coordinator.RecordingStarted += (_, session) => started = session;

        await coordinator.StartAsync(calendarEvent, settings, CancellationToken.None);

        var expectedPath = Path.Combine(temp.Path, "06.05.2026 18.42.wav");
        Assert.True(coordinator.IsRecording);
        Assert.NotNull(coordinator.CurrentSession);
        Assert.Equal(calendarEvent, coordinator.CurrentSession.CalendarEvent);
        Assert.Equal(now, coordinator.CurrentSession.StartedAt);
        Assert.Equal(expectedPath, coordinator.CurrentSession.OutputPath);
        Assert.Equal(expectedPath, recorder.StartedPath);
        Assert.Same(coordinator.CurrentSession, started);
    }

    [Fact]
    public async Task StartIsNoOpWhenAlreadyRecording()
    {
        var now = new DateTimeOffset(2026, 5, 6, 18, 42, 0, TimeSpan.Zero);
        using var temp = new TempFolder();
        var recorder = new FakeAudioRecorder();
        var factoryCalls = 0;
        var coordinator = new RecordingCoordinator(
            () =>
            {
                factoryCalls++;
                return recorder;
            },
            () => now);

        await coordinator.StartAsync(CreateEvent(now), CreateSettings(temp.Path), CancellationToken.None);
        await coordinator.StartAsync(CreateEvent(now.AddHours(1)), CreateSettings(temp.Path), CancellationToken.None);

        Assert.Equal(1, factoryCalls);
        Assert.Equal(1, recorder.StartCalls);
    }

    [Fact]
    public async Task ConfirmStopStopsDisposesAndRaisesSaved()
    {
        var now = new DateTimeOffset(2026, 5, 6, 18, 42, 0, TimeSpan.Zero);
        using var temp = new TempFolder();
        var recorder = new FakeAudioRecorder();
        var coordinator = new RecordingCoordinator(() => recorder, () => now);
        RecordingSession? saved = null;

        coordinator.RecordingSaved += (_, session) => saved = session;

        await coordinator.StartAsync(CreateEvent(now), CreateSettings(temp.Path), CancellationToken.None);
        var session = coordinator.CurrentSession;
        await coordinator.ConfirmStopAsync(CancellationToken.None);

        Assert.False(coordinator.IsRecording);
        Assert.Null(coordinator.CurrentSession);
        Assert.Equal(1, recorder.StopCalls);
        Assert.Equal(1, recorder.DisposeCalls);
        Assert.Same(session, saved);
    }

    [Fact]
    public async Task StartFailureClearsSessionAndDisposesRecorder()
    {
        var now = new DateTimeOffset(2026, 5, 6, 18, 42, 0, TimeSpan.Zero);
        using var temp = new TempFolder();
        var recorder = new FakeAudioRecorder
        {
            StartHandler = (_, _) => throw new InvalidOperationException("start failed")
        };
        var coordinator = new RecordingCoordinator(() => recorder, () => now);
        var started = 0;
        var prompts = 0;
        var saved = 0;

        coordinator.RecordingStarted += (_, _) => started++;
        coordinator.StopPromptRequired += (_, _) => prompts++;
        coordinator.RecordingSaved += (_, _) => saved++;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.StartAsync(CreateEvent(now), CreateSettings(temp.Path), CancellationToken.None));

        Assert.False(coordinator.IsRecording);
        Assert.Null(coordinator.CurrentSession);
        Assert.Equal(1, recorder.DisposeCalls);

        recorder.RaiseLevel(new AudioLevel(0, 0));
        now = now.AddMinutes(1);
        recorder.RaiseLevel(new AudioLevel(0, 0));

        Assert.Equal(0, started);
        Assert.Equal(0, prompts);
        Assert.Equal(0, saved);
    }

    [Fact]
    public async Task LevelAfterStopDoesNotRaiseStopPrompt()
    {
        var now = new DateTimeOffset(2026, 5, 6, 18, 42, 0, TimeSpan.Zero);
        using var temp = new TempFolder();
        var recorder = new FakeAudioRecorder();
        var coordinator = new RecordingCoordinator(() => recorder, () => now);
        var prompts = 0;

        coordinator.StopPromptRequired += (_, _) => prompts++;

        await coordinator.StartAsync(CreateEvent(now), CreateSettings(temp.Path), CancellationToken.None);
        await coordinator.ConfirmStopAsync(CancellationToken.None);

        recorder.RaiseLevel(new AudioLevel(0, 0));
        now = now.AddMinutes(1);
        recorder.RaiseLevel(new AudioLevel(0, 0));

        Assert.Equal(0, prompts);
    }

    [Fact]
    public async Task ConfirmStopWaitsForPendingStart()
    {
        var now = new DateTimeOffset(2026, 5, 6, 18, 42, 0, TimeSpan.Zero);
        using var temp = new TempFolder();
        var startEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recorder = new FakeAudioRecorder
        {
            StartHandler = (_, _) =>
            {
                startEntered.SetResult();
                return allowStart.Task;
            }
        };
        var coordinator = new RecordingCoordinator(() => recorder, () => now);
        var events = new List<string>();
        var nullStartedSession = false;
        var nullSavedSession = false;

        coordinator.RecordingStarted += (_, session) =>
        {
            nullStartedSession = session is null;
            events.Add("started");
        };
        coordinator.RecordingSaved += (_, session) =>
        {
            nullSavedSession = session is null;
            events.Add("saved");
        };

        var startTask = coordinator.StartAsync(CreateEvent(now), CreateSettings(temp.Path), CancellationToken.None);
        await startEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var stopTask = coordinator.ConfirmStopAsync(CancellationToken.None);

        Assert.False(stopTask.IsCompleted);

        allowStart.SetResult();
        await startTask;
        await stopTask;

        Assert.False(coordinator.IsRecording);
        Assert.Null(coordinator.CurrentSession);
        Assert.Equal(1, recorder.StopCalls);
        Assert.Equal(1, recorder.DisposeCalls);
        Assert.False(nullStartedSession);
        Assert.False(nullSavedSession);
        Assert.Equal(new[] { "started", "saved" }, events);
    }

    [Fact]
    public async Task SilentLevelsRaiseStopPromptAfterConfiguredInterval()
    {
        var now = new DateTimeOffset(2026, 5, 6, 18, 42, 0, TimeSpan.Zero);
        using var temp = new TempFolder();
        var recorder = new FakeAudioRecorder();
        var coordinator = new RecordingCoordinator(() => recorder, () => now);
        var prompts = 0;

        coordinator.StopPromptRequired += (_, _) => prompts++;

        await coordinator.StartAsync(CreateEvent(now), CreateSettings(temp.Path), CancellationToken.None);
        recorder.RaiseLevel(new AudioLevel(0, 0));
        now = now.AddMinutes(1);
        recorder.RaiseLevel(new AudioLevel(0, 0));

        Assert.Equal(1, prompts);
    }

    [Fact]
    public async Task DeclineStopDelaysNextPrompt()
    {
        var now = new DateTimeOffset(2026, 5, 6, 18, 42, 0, TimeSpan.Zero);
        using var temp = new TempFolder();
        var recorder = new FakeAudioRecorder();
        var coordinator = new RecordingCoordinator(() => recorder, () => now);
        var prompts = 0;

        coordinator.StopPromptRequired += (_, _) => prompts++;

        await coordinator.StartAsync(CreateEvent(now), CreateSettings(temp.Path), CancellationToken.None);
        recorder.RaiseLevel(new AudioLevel(0, 0));
        now = now.AddMinutes(1);
        recorder.RaiseLevel(new AudioLevel(0, 0));
        coordinator.DeclineStop();
        now = now.AddMinutes(5);
        recorder.RaiseLevel(new AudioLevel(0, 0));
        now = now.AddMinutes(1);
        recorder.RaiseLevel(new AudioLevel(0, 0));

        Assert.Equal(2, prompts);
    }

    [Fact]
    public async Task IgnoreStopPromptAllowsPromptAgainWithoutRetry()
    {
        var now = new DateTimeOffset(2026, 5, 6, 18, 42, 0, TimeSpan.Zero);
        using var temp = new TempFolder();
        var recorder = new FakeAudioRecorder();
        var coordinator = new RecordingCoordinator(() => recorder, () => now);
        var prompts = 0;

        coordinator.StopPromptRequired += (_, _) => prompts++;

        await coordinator.StartAsync(CreateEvent(now), CreateSettings(temp.Path), CancellationToken.None);
        recorder.RaiseLevel(new AudioLevel(0, 0));
        now = now.AddMinutes(1);
        recorder.RaiseLevel(new AudioLevel(0, 0));
        coordinator.IgnoreStopPrompt();
        recorder.RaiseLevel(new AudioLevel(0, 0));

        Assert.Equal(2, prompts);
    }

    private static CalendarEvent CreateEvent(DateTimeOffset startsAt) =>
        new("Planning", startsAt, startsAt.AddMinutes(30));

    private static AppSettings CreateSettings(string outputFolder) =>
        new()
        {
            OutputFolder = outputFolder,
            SilencePromptMinutes = 1,
            RetryPromptMinutes = 5
        };

    private sealed class FakeAudioRecorder : IAudioRecorder
    {
        public event EventHandler<AudioLevel>? LevelChanged;

        public int DisposeCalls { get; private set; }
        public string? StartedPath { get; private set; }
        public int StartCalls { get; private set; }
        public Func<string, CancellationToken, Task>? StartHandler { get; init; }
        public int StopCalls { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return ValueTask.CompletedTask;
        }

        public Task StartAsync(string outputPath, CancellationToken cancellationToken)
        {
            StartCalls++;
            StartedPath = outputPath;
            return StartHandler?.Invoke(outputPath, cancellationToken) ?? Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCalls++;
            return Task.CompletedTask;
        }

        public void RaiseLevel(AudioLevel level) => LevelChanged?.Invoke(this, level);
    }

    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
