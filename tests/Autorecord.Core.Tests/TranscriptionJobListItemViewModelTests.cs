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
        Assert.Equal("Без разделения", item.DiarizationModel);
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

    [Fact]
    public void FromJobShowsDiarizationModel()
    {
        var item = TranscriptionJobListItemViewModel.FromJob(new TranscriptionJob
        {
            Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            InputFilePath = "C:\\Records\\meeting.wav",
            OutputDirectory = "C:\\Transcripts",
            AsrModelId = "gigaam-v3-ru-quality",
            DiarizationModelId = "pyannote-community-1",
            Status = TranscriptionJobStatus.Completed,
            ProgressPercent = 100,
            CreatedAt = DateTimeOffset.Parse("2026-05-07T10:00:00+03:00")
        });

        Assert.Equal("pyannote-community-1", item.DiarizationModel);
    }

    [Fact]
    public void FromJobShowsPublicModelNameForGigaAm()
    {
        var item = TranscriptionJobListItemViewModel.FromJob(new TranscriptionJob
        {
            Id = Guid.Parse("77777777-7777-7777-7777-777777777777"),
            InputFilePath = "C:\\Records\\meeting.wav",
            OutputDirectory = "C:\\Transcripts",
            AsrModelId = "gigaam-v3-ru-quality",
            Status = TranscriptionJobStatus.Pending,
            CreatedAt = DateTimeOffset.Parse("2026-05-07T10:00:00+03:00")
        });

        Assert.Equal("GigaAM v3", item.Model);
    }

    [Fact]
    public void FromJobShowsRunningDiarizationStageAtTenPercent()
    {
        var item = TranscriptionJobListItemViewModel.FromJob(new TranscriptionJob
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
            InputFilePath = "C:\\Records\\meeting.wav",
            OutputDirectory = "C:\\Transcripts",
            AsrModelId = "gigaam-v3-ru-quality",
            DiarizationModelId = "pyannote-community-1",
            Status = TranscriptionJobStatus.Running,
            ProgressPercent = 10,
            CreatedAt = DateTimeOffset.Parse("2026-05-07T10:00:00+03:00")
        });

        Assert.Contains("Чтение файла: готово", item.StageLines, StringComparison.Ordinal);
        Assert.Contains("Диаризация: выполняется", item.StageLines, StringComparison.Ordinal);
        Assert.Contains("Транскрибация: ожидает", item.StageLines, StringComparison.Ordinal);
        Assert.Contains("Сохранение транскрипта: ожидает", item.StageLines, StringComparison.Ordinal);
    }

    [Fact]
    public void FromJobSkipsDiarizationStageWhenJobHasNoDiarizationModel()
    {
        var item = TranscriptionJobListItemViewModel.FromJob(new TranscriptionJob
        {
            Id = Guid.Parse("88888888-8888-8888-8888-888888888888"),
            InputFilePath = "C:\\Records\\meeting.wav",
            OutputDirectory = "C:\\Transcripts",
            AsrModelId = "gigaam-v3-ru-quality",
            DiarizationModelId = null,
            Status = TranscriptionJobStatus.Running,
            ProgressPercent = 10,
            CreatedAt = DateTimeOffset.Parse("2026-05-07T10:00:00+03:00")
        });

        Assert.Contains("Чтение файла: готово", item.StageLines, StringComparison.Ordinal);
        Assert.DoesNotContain("Диаризация", item.StageLines, StringComparison.Ordinal);
        Assert.Contains("Транскрибация: выполняется", item.StageLines, StringComparison.Ordinal);
        Assert.Contains("Сохранение транскрипта: ожидает", item.StageLines, StringComparison.Ordinal);
    }

    [Fact]
    public void FromJobShowsFailedStageFromProgress()
    {
        var item = TranscriptionJobListItemViewModel.FromJob(new TranscriptionJob
        {
            Id = Guid.Parse("66666666-6666-6666-6666-666666666666"),
            InputFilePath = "C:\\Records\\meeting.wav",
            OutputDirectory = "C:\\Transcripts",
            AsrModelId = "gigaam-v3-ru-quality",
            DiarizationModelId = "pyannote-community-1",
            Status = TranscriptionJobStatus.Failed,
            ProgressPercent = 0,
            CreatedAt = DateTimeOffset.Parse("2026-05-07T10:00:00+03:00"),
            ErrorMessage = "Invalid WAV file"
        });

        Assert.Contains("Чтение файла: ошибка", item.StageLines, StringComparison.Ordinal);
        Assert.Contains("Диаризация: ожидает", item.StageLines, StringComparison.Ordinal);
    }
}
