using Autorecord.App.Transcription;
using Autorecord.Core.Transcription.Jobs;

namespace Autorecord.Core.Tests;

public sealed class TranscriptionJobListItemViewModelTests
{
    [Fact]
    public void FromJobEnablesOpenActionsForCompletedJobWithOutputFiles()
    {
        var item = TranscriptionJobListItemViewModel.FromJob(new TranscriptionJob
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            InputFilePath = "C:\\Records\\meeting.wav",
            OutputDirectory = "C:\\Transcripts",
            AsrModelId = "asr-fast",
            Status = TranscriptionJobStatus.Completed,
            ProgressPercent = 100,
            CreatedAt = DateTimeOffset.Parse("2026-05-07T10:00:00+03:00"),
            FinishedAt = DateTimeOffset.Parse("2026-05-07T10:05:00+03:00"),
            OutputFiles = ["C:\\Transcripts\\meeting.md"]
        });

        Assert.Equal("11111111-1111-1111-1111-111111111111", item.Id.ToString());
        Assert.Equal("asr-fast", item.Model);
        Assert.True(item.CanOpenTranscript);
        Assert.True(item.CanOpenFolder);
        Assert.True(item.CanRetry);
        Assert.False(item.CanCancel);
        Assert.True(item.CanDelete);
    }

    [Fact]
    public void FromJobEnablesCancelOnlyForRunningJob()
    {
        var item = TranscriptionJobListItemViewModel.FromJob(new TranscriptionJob
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            InputFilePath = "C:\\Records\\meeting.wav",
            OutputDirectory = "C:\\Transcripts",
            AsrModelId = "asr-fast",
            Status = TranscriptionJobStatus.Running,
            ProgressPercent = 42,
            CreatedAt = DateTimeOffset.Parse("2026-05-07T10:00:00+03:00")
        });

        Assert.False(item.CanOpenTranscript);
        Assert.True(item.CanOpenFolder);
        Assert.False(item.CanRetry);
        Assert.True(item.CanCancel);
        Assert.False(item.CanDelete);
    }

    [Fact]
    public void FromJobShowsFailedErrorInStatus()
    {
        var item = TranscriptionJobListItemViewModel.FromJob(new TranscriptionJob
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            InputFilePath = "C:\\Records\\meeting.wav",
            OutputDirectory = "C:\\Transcripts",
            AsrModelId = "asr-fast",
            Status = TranscriptionJobStatus.Failed,
            ProgressPercent = 0,
            CreatedAt = DateTimeOffset.Parse("2026-05-07T10:00:00+03:00"),
            ErrorMessage = "model missing"
        });

        Assert.Equal("Failed: model missing", item.Status);
        Assert.True(item.CanRetry);
        Assert.True(item.CanCancel is false);
        Assert.True(item.CanDelete);
    }
}
