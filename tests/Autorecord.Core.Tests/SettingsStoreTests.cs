using Autorecord.Core.Settings;

namespace Autorecord.Core.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public async Task SaveAndLoadRoundTripsSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        var store = new SettingsStore(path);
        var settings = new AppSettings
        {
            CalendarUrl = "https://example.com/calendar.ics",
            OutputFolder = "C:\\Records",
            RecordingMode = RecordingMode.TaggedEvents,
            EventTag = "запись",
            SilencePromptMinutes = 2,
            RetryPromptMinutes = 10,
            StartWithWindows = true
        };

        await store.SaveAsync(settings, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(settings, loaded);
    }

    [Fact]
    public async Task LoadReturnsDefaultsWhenFileDoesNotExist()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        var store = new SettingsStore(path);

        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(1, loaded.SilencePromptMinutes);
        Assert.Equal(5, loaded.RetryPromptMinutes);
        Assert.Equal(RecordingMode.AllEvents, loaded.RecordingMode);
    }
}
