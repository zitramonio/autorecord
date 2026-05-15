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
            AutoStartRecordingFromCalendar = false,
            AutoStopRecordingOnSilence = false,
            SilencePromptMinutes = 2,
            RetryPromptMinutes = 10,
            NoAnswerStopPromptMinutes = 3,
            NotificationsEnabled = false,
            StartWithWindows = true
        };

        await store.SaveAsync(settings, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(settings, loaded);
    }

    [Fact]
    public async Task SaveAndLoadRoundTripsTranscriptionSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        var store = new SettingsStore(path);
        var settings = new AppSettings
        {
            Transcription = new TranscriptionSettings
            {
                AutoTranscribeAfterRecording = true,
                SelectedAsrModelId = "custom-asr",
                SelectedDiarizationModelId = "custom-diarization",
                OutputFolderMode = TranscriptOutputFolderMode.CustomFolder,
                CustomOutputFolder = "C:\\Transcripts",
                OutputFormats = [TranscriptOutputFormat.Markdown, TranscriptOutputFormat.Json],
                EnableDiarization = true,
                NumSpeakers = 2,
                ClusterThreshold = 0.75,
                OverwriteExistingTranscripts = true,
                KeepIntermediateFiles = true
            }
        };

        await store.SaveAsync(settings, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(settings.Transcription, loaded.Transcription);
    }

    [Fact]
    public async Task LoadReturnsDefaultsWhenFileDoesNotExist()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        var store = new SettingsStore(path);

        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(1, loaded.SilencePromptMinutes);
        Assert.Equal(5, loaded.RetryPromptMinutes);
        Assert.Equal(2, loaded.NoAnswerStopPromptMinutes);
        Assert.True(loaded.AutoStartRecordingFromCalendar);
        Assert.True(loaded.AutoStopRecordingOnSilence);
        Assert.True(loaded.NotificationsEnabled);
        Assert.Equal(RecordingMode.AllEvents, loaded.RecordingMode);
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Autorecord"),
            loaded.OutputFolder);
        Assert.Equal(new TranscriptionSettings(), loaded.Transcription);
    }

    [Fact]
    public async Task LoadUsesDefaultTranscriptionSettingsWhenSectionIsMissing()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            """
            {
              "CalendarUrl": "https://example.com/calendar.ics",
              "OutputFolder": "C:\\Records",
              "RecordingMode": 1,
              "EventTag": "record",
              "AutoStartRecordingFromCalendar": false,
              "AutoStopRecordingOnSilence": false,
              "SilencePromptMinutes": 2,
              "RetryPromptMinutes": 10,
              "NoAnswerStopPromptMinutes": 3,
              "NotificationsEnabled": false,
              "KeepMicrophoneReady": true,
              "StartWithWindows": true
            }
            """);
        var store = new SettingsStore(path);

        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(new TranscriptionSettings(), loaded.Transcription);
        Assert.False(loaded.AutoStartRecordingFromCalendar);
        Assert.False(loaded.AutoStopRecordingOnSilence);
        Assert.Equal(3, loaded.NoAnswerStopPromptMinutes);
        Assert.False(loaded.NotificationsEnabled);
        Assert.True(loaded.Transcription.AutoTranscribeAfterRecording);
        Assert.True(loaded.Transcription.EnableDiarization);
        Assert.Equal("gigaam-v3-ru-quality", loaded.Transcription.SelectedAsrModelId);
        Assert.Equal("pyannote-community-1", loaded.Transcription.SelectedDiarizationModelId);
        Assert.Equal(
            [TranscriptOutputFormat.Txt],
            loaded.Transcription.OutputFormats);
    }

    [Fact]
    public async Task LoadRejectsNullTranscriptionSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            """
            {
              "Transcription": null
            }
            """);
        var store = new SettingsStore(path);

        await Assert.ThrowsAsync<ArgumentException>(() => store.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LoadRejectsNullTranscriptOutputFormats()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            """
            {
              "Transcription": {
                "OutputFormats": null
              }
            }
            """);
        var store = new SettingsStore(path);

        await Assert.ThrowsAsync<ArgumentException>(() => store.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SaveRejectsBlankSelectedAsrModelId()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        var store = new SettingsStore(path);
        var settings = new AppSettings
        {
            Transcription = new TranscriptionSettings
            {
                SelectedAsrModelId = " "
            }
        };

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(settings, CancellationToken.None));
    }

    [Fact]
    public async Task SaveAllowsEmptyDiarizationModelWhenDiarizationDisabled()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        var store = new SettingsStore(path);
        var settings = new AppSettings
        {
            Transcription = new TranscriptionSettings
            {
                EnableDiarization = false,
                SelectedDiarizationModelId = ""
            }
        };

        await store.SaveAsync(settings, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.False(loaded.Transcription.EnableDiarization);
        Assert.Equal("", loaded.Transcription.SelectedDiarizationModelId);
    }

    [Fact]
    public async Task SaveRejectsEmptyDiarizationModelWhenDiarizationEnabled()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        var store = new SettingsStore(path);
        var settings = new AppSettings
        {
            Transcription = new TranscriptionSettings
            {
                EnableDiarization = true,
                SelectedDiarizationModelId = ""
            }
        };

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(settings, CancellationToken.None));
    }

    [Fact]
    public async Task SaveRejectsCustomTranscriptFolderWithoutPath()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        var store = new SettingsStore(path);
        var settings = new AppSettings
        {
            Transcription = new TranscriptionSettings
            {
                OutputFolderMode = TranscriptOutputFolderMode.CustomFolder,
                CustomOutputFolder = ""
            }
        };

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(settings, CancellationToken.None));
    }

    [Fact]
    public async Task SaveRejectsInvalidSpeakerCount()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        var store = new SettingsStore(path);
        var settings = new AppSettings
        {
            Transcription = new TranscriptionSettings
            {
                NumSpeakers = 7
            }
        };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SaveAsync(settings, CancellationToken.None));
    }

    [Fact]
    public async Task LoadRejectsNonPositiveIntervals()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            """
            {
              "SilencePromptMinutes": 0,
              "RetryPromptMinutes": 0,
              "NoAnswerStopPromptMinutes": 0
            }
            """);
        var store = new SettingsStore(path);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SaveRejectsUnknownRecordingMode()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        var store = new SettingsStore(path);
        var settings = new AppSettings { RecordingMode = (RecordingMode)999 };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SaveAsync(settings, CancellationToken.None));
    }

    [Fact]
    public async Task LoadRejectsUnknownRecordingMode()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            """
            {
              "RecordingMode": 999
            }
            """);
        var store = new SettingsStore(path);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.LoadAsync(CancellationToken.None));
    }
}
