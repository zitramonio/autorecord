using System.Reflection;
using Autorecord.Core.Settings;
using Autorecord.Core.Transcription.Jobs;

namespace Autorecord.Core.Tests;

public sealed class CurrentTranscriptionSelectionTests
{
    [Fact]
    public void SelectCurrentTranscriptionJobIgnoresCancelledJobs()
    {
        var cancelled = CreateReleaseJob(TranscriptionJobStatus.Cancelled) with
        {
            FinishedAt = DateTimeOffset.Parse("2026-05-13T12:15:03+03:00"),
            ErrorMessage = "Transcription job was cancelled."
        };

        var selected = SelectCurrentTranscriptionJob([cancelled]);

        Assert.Null(selected);
    }

    [Fact]
    public void SelectCurrentTranscriptionJobStillKeepsCompletedJobVisible()
    {
        var completed = CreateReleaseJob(TranscriptionJobStatus.Completed) with
        {
            FinishedAt = DateTimeOffset.Parse("2026-05-13T12:18:03+03:00"),
            ProgressPercent = 100,
            OutputFiles = ["C:\\Users\\User\\Documents\\Autorecord\\13.05.2026 12.14.md"]
        };

        var selected = SelectCurrentTranscriptionJob([completed]);

        Assert.Equal(completed.Id, selected?.Id);
    }

    [Fact]
    public void SelectCurrentTranscriptionJobKeepsParakeetJobVisible()
    {
        var completed = CreateReleaseJob(TranscriptionJobStatus.Completed) with
        {
            AsrModelId = "sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8",
            FinishedAt = DateTimeOffset.Parse("2026-05-13T12:18:03+03:00"),
            ProgressPercent = 100,
            OutputFiles = ["C:\\Users\\User\\Documents\\Autorecord\\13.05.2026 12.14.md"]
        };

        var selected = SelectCurrentTranscriptionJob([completed]);

        Assert.Equal(completed.Id, selected?.Id);
    }

    private static TranscriptionJob? SelectCurrentTranscriptionJob(IReadOnlyList<TranscriptionJob> jobs)
    {
        var method = typeof(Autorecord.App.App).GetMethod(
            "SelectCurrentTranscriptionJob",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (TranscriptionJob?)method.Invoke(null, [jobs]);
    }

    private static TranscriptionJob CreateReleaseJob(TranscriptionJobStatus status)
    {
        return new TranscriptionJob
        {
            Id = Guid.NewGuid(),
            InputFilePath = "C:\\Users\\User\\Documents\\Autorecord\\13.05.2026 12.14.mp3",
            OutputDirectory = "C:\\Users\\User\\Documents\\Autorecord",
            AsrModelId = AutorecordDefaults.AsrModelId,
            DiarizationModelId = AutorecordDefaults.DiarizationModelId,
            Status = status,
            CreatedAt = DateTimeOffset.Parse("2026-05-13T12:14:54+03:00")
        };
    }
}
