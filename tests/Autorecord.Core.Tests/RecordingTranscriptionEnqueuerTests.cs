using Autorecord.Core.Calendar;
using Autorecord.Core.Recording;
using Autorecord.Core.Settings;
using Autorecord.Core.Transcription;

namespace Autorecord.Core.Tests;

public sealed class RecordingTranscriptionEnqueuerTests
{
    [Fact]
    public async Task EnqueueAsyncDoesNothingWhenAutoTranscribeIsDisabled()
    {
        var enqueueCalls = 0;
        var session = CreateSession("C:\\Records\\meeting.mp3");

        await RecordingTranscriptionEnqueuer.EnqueueAsync(
            session,
            new TranscriptionSettings { AutoTranscribeAfterRecording = false },
            ResolveOutputDirectory,
            (_, _, _, _, _) =>
            {
                enqueueCalls++;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(0, enqueueCalls);
    }

    [Fact]
    public async Task EnqueueAsyncPassesRecordingPathCustomOutputAndSelectedModels()
    {
        EnqueuedRecording? request = null;
        var session = CreateSession("C:\\Records\\meeting.mp3");
        var settings = new TranscriptionSettings
        {
            AutoTranscribeAfterRecording = true,
            SelectedAsrModelId = "asr-fast",
            SelectedDiarizationModelId = "diarization-fast",
            EnableDiarization = true,
            OutputFolderMode = TranscriptOutputFolderMode.CustomFolder,
            CustomOutputFolder = "D:\\Transcripts"
        };

        await RecordingTranscriptionEnqueuer.EnqueueAsync(
            session,
            settings,
            ResolveOutputDirectory,
            (inputFilePath, outputDirectory, asrModelId, diarizationModelId, _) =>
            {
                request = new EnqueuedRecording(
                    inputFilePath,
                    outputDirectory,
                    asrModelId,
                    diarizationModelId);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.NotNull(request);
        Assert.Equal("C:\\Records\\meeting.mp3", request.InputFilePath);
        Assert.Equal("D:\\Transcripts", request.OutputDirectory);
        Assert.Equal("asr-fast", request.AsrModelId);
        Assert.Equal("diarization-fast", request.DiarizationModelId);
    }

    [Fact]
    public async Task EnqueueAsyncOmitsDiarizationModelWhenDiarizationIsDisabled()
    {
        EnqueuedRecording? request = null;
        var session = CreateSession("C:\\Records\\meeting.mp3");
        var settings = new TranscriptionSettings
        {
            AutoTranscribeAfterRecording = true,
            SelectedAsrModelId = "asr-fast",
            SelectedDiarizationModelId = "diarization-fast",
            EnableDiarization = false
        };

        await RecordingTranscriptionEnqueuer.EnqueueAsync(
            session,
            settings,
            ResolveOutputDirectory,
            (inputFilePath, outputDirectory, asrModelId, diarizationModelId, _) =>
            {
                request = new EnqueuedRecording(
                    inputFilePath,
                    outputDirectory,
                    asrModelId,
                    diarizationModelId);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.NotNull(request);
        Assert.Null(request.DiarizationModelId);
    }

    [Fact]
    public async Task EnqueueAsyncUsesRecordingFolderWhenOutputModeIsSameAsRecording()
    {
        EnqueuedRecording? request = null;
        var session = CreateSession("C:\\Records\\meeting.mp3");
        var settings = new TranscriptionSettings
        {
            AutoTranscribeAfterRecording = true,
            SelectedAsrModelId = "asr-fast",
            OutputFolderMode = TranscriptOutputFolderMode.SameAsRecording
        };

        await RecordingTranscriptionEnqueuer.EnqueueAsync(
            session,
            settings,
            ResolveOutputDirectory,
            (inputFilePath, outputDirectory, asrModelId, diarizationModelId, _) =>
            {
                request = new EnqueuedRecording(
                    inputFilePath,
                    outputDirectory,
                    asrModelId,
                    diarizationModelId);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.NotNull(request);
        Assert.Equal("C:\\Records", request.OutputDirectory);
    }

    private static RecordingSession CreateSession(string outputPath)
    {
        var startedAt = DateTimeOffset.Parse("2026-05-07T10:00:00+03:00");
        return new RecordingSession(new CalendarEvent("Meeting", startedAt, startedAt), startedAt, outputPath);
    }

    private static string ResolveOutputDirectory(string inputFilePath, TranscriptionSettings settings)
    {
        return settings.OutputFolderMode == TranscriptOutputFolderMode.CustomFolder
            ? settings.CustomOutputFolder!
            : Path.GetDirectoryName(inputFilePath)!;
    }

    private sealed record EnqueuedRecording(
        string InputFilePath,
        string OutputDirectory,
        string AsrModelId,
        string? DiarizationModelId);
}
