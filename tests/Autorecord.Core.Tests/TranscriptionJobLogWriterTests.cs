using Autorecord.Core.Transcription.Jobs;
using Autorecord.Core.Transcription.Pipeline;

namespace Autorecord.Core.Tests;

public sealed class TranscriptionJobLogWriterTests
{
    [Fact]
    public async Task WriteStartedAsyncCreatesLogWithJobMetadata()
    {
        var root = CreateTempRoot();
        var writer = new TranscriptionJobLogWriter(root);
        var job = CreateJob();

        await writer.WriteStartedAsync(job, CancellationToken.None);

        var log = await File.ReadAllTextAsync(writer.GetLogPath(job.Id));
        Assert.Contains($"JobId: {job.Id}", log);
        Assert.Contains("InputFilePath: C:\\Records\\meeting.wav", log);
        Assert.Contains("AsrModelId: asr-fast", log);
        Assert.Contains("DiarizationModelId: diarization-fast", log);
        Assert.Contains("StartedAt: 2026-05-07T10:00:00.0000000+03:00", log);
        Assert.DoesNotContain("секретный текст транскрипта", log);
    }

    [Fact]
    public async Task WriteFinishedAsyncAppendsStatusDurationProcessingTimeAndOutputs()
    {
        var root = CreateTempRoot();
        var writer = new TranscriptionJobLogWriter(root);
        var started = CreateJob();
        var finished = started with
        {
            Status = TranscriptionJobStatus.Completed,
            FinishedAt = DateTimeOffset.Parse("2026-05-07T10:00:09+03:00"),
            OutputFiles = ["C:\\Transcripts\\meeting.txt", "C:\\Transcripts\\meeting.json"]
        };

        await writer.WriteStartedAsync(started, CancellationToken.None);
        await writer.WriteFinishedAsync(
            finished,
            new TranscriptionPipelineResult(finished.OutputFiles, 12.34),
            TimeSpan.FromSeconds(9),
            CancellationToken.None);

        var log = await File.ReadAllTextAsync(writer.GetLogPath(finished.Id));
        Assert.Contains("Status: Completed", log);
        Assert.Contains("FinishedAt: 2026-05-07T10:00:09.0000000+03:00", log);
        Assert.Contains("DurationSec: 12.34", log);
        Assert.Contains("ProcessingTime: 00:00:09", log);
        Assert.Contains("OutputFile: C:\\Transcripts\\meeting.txt", log);
        Assert.Contains("OutputFile: C:\\Transcripts\\meeting.json", log);
    }

    private static TranscriptionJob CreateJob()
    {
        return new TranscriptionJob
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            InputFilePath = "C:\\Records\\meeting.wav",
            OutputDirectory = "C:\\Transcripts",
            AsrModelId = "asr-fast",
            DiarizationModelId = "diarization-fast",
            Status = TranscriptionJobStatus.Running,
            ProgressPercent = 0,
            CreatedAt = DateTimeOffset.Parse("2026-05-07T09:59:00+03:00"),
            StartedAt = DateTimeOffset.Parse("2026-05-07T10:00:00+03:00")
        };
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
