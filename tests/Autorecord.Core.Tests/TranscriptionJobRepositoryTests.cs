using Autorecord.Core.Transcription.Jobs;

namespace Autorecord.Core.Tests;

public sealed class TranscriptionJobRepositoryTests
{
    [Fact]
    public async Task SaveAndLoadRoundTripsJobs()
    {
        var path = CreateTempPath();
        var repository = new TranscriptionJobRepository(path);
        var job = CreateJob() with
        {
            DiarizationModelId = "diarization",
            Status = TranscriptionJobStatus.Completed,
            ProgressPercent = 100,
            StartedAt = DateTimeOffset.Parse("2026-05-07T10:00:00+03:00"),
            FinishedAt = DateTimeOffset.Parse("2026-05-07T10:05:00+03:00"),
            OutputFiles = ["C:\\Transcripts\\meeting.md", "C:\\Transcripts\\meeting.json"]
        };

        await repository.SaveAsync([job], CancellationToken.None);
        var loaded = await repository.LoadAsync(CancellationToken.None);

        var loadedJob = Assert.Single(loaded);
        Assert.Equal(job.Id, loadedJob.Id);
        Assert.Equal(job.InputFilePath, loadedJob.InputFilePath);
        Assert.Equal(job.OutputDirectory, loadedJob.OutputDirectory);
        Assert.Equal(job.AsrModelId, loadedJob.AsrModelId);
        Assert.Equal(job.DiarizationModelId, loadedJob.DiarizationModelId);
        Assert.Equal(job.Status, loadedJob.Status);
        Assert.Equal(job.ProgressPercent, loadedJob.ProgressPercent);
        Assert.Equal(job.CreatedAt, loadedJob.CreatedAt);
        Assert.Equal(job.StartedAt, loadedJob.StartedAt);
        Assert.Equal(job.FinishedAt, loadedJob.FinishedAt);
        Assert.Equal(job.ErrorMessage, loadedJob.ErrorMessage);
        Assert.Equal(job.OutputFiles, loadedJob.OutputFiles);
    }

    [Fact]
    public async Task LoadConvertsRunningJobsToPending()
    {
        var path = CreateTempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            """
            [
              {
                "id": "11111111-1111-1111-1111-111111111111",
                "inputFilePath": "C:\\Records\\meeting.wav",
                "outputDirectory": "C:\\Transcripts",
                "asrModelId": "asr-fast",
                "status": 2,
                "progressPercent": 40,
                "createdAt": "2026-05-07T10:00:00+03:00",
                "startedAt": "2026-05-07T10:01:00+03:00",
                "outputFiles": []
              }
            ]
            """);
        var repository = new TranscriptionJobRepository(path);

        var loaded = await repository.LoadAsync(CancellationToken.None);

        var job = Assert.Single(loaded);
        Assert.Equal(TranscriptionJobStatus.Pending, job.Status);
        Assert.Null(job.StartedAt);
        Assert.Contains("interrupted", job.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadReturnsEmptyWhenFileDoesNotExist()
    {
        var repository = new TranscriptionJobRepository(CreateTempPath());

        var loaded = await repository.LoadAsync(CancellationToken.None);

        Assert.Empty(loaded);
    }

    [Fact]
    public async Task LoadRejectsNullJobsArray()
    {
        var path = CreateTempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "null");
        var repository = new TranscriptionJobRepository(path);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LoadRejectsInvalidStatus()
    {
        var path = CreateTempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            """
            [
              {
                "id": "11111111-1111-1111-1111-111111111111",
                "inputFilePath": "C:\\Records\\meeting.wav",
                "outputDirectory": "C:\\Transcripts",
                "asrModelId": "asr-fast",
                "status": 999,
                "progressPercent": 0,
                "createdAt": "2026-05-07T10:00:00+03:00",
                "outputFiles": []
              }
            ]
            """);
        var repository = new TranscriptionJobRepository(path);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SaveRejectsInvalidProgress()
    {
        var repository = new TranscriptionJobRepository(CreateTempPath());
        var job = CreateJob() with { ProgressPercent = 101 };

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveAsync([job], CancellationToken.None));
    }

    [Fact]
    public async Task SaveRejectsBlankRequiredPaths()
    {
        var repository = new TranscriptionJobRepository(CreateTempPath());
        var job = CreateJob() with { OutputDirectory = " " };

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveAsync([job], CancellationToken.None));
    }

    private static TranscriptionJob CreateJob()
    {
        return new TranscriptionJob
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            InputFilePath = "C:\\Records\\meeting.wav",
            OutputDirectory = "C:\\Transcripts",
            AsrModelId = "asr-fast",
            Status = TranscriptionJobStatus.Pending,
            ProgressPercent = 0,
            CreatedAt = DateTimeOffset.Parse("2026-05-07T10:00:00+03:00")
        };
    }

    private static string CreateTempPath()
    {
        return Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "jobs.json");
    }
}
