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
    public async Task LoadRejectsNullJobElements()
    {
        var path = CreateTempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "[null]");
        var repository = new TranscriptionJobRepository(path);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LoadRejectsInvalidStatus()
    {
        var path = CreateTempPath();
        await WriteJobJsonAsync(path, status: "999");
        var repository = new TranscriptionJobRepository(path);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LoadRejectsNullOutputFiles()
    {
        var path = CreateTempPath();
        await WriteJobJsonAsync(path, outputFiles: "null");
        var repository = new TranscriptionJobRepository(path);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.LoadAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("\" \"", "\"C:\\\\Transcripts\"", "\"asr-fast\"")]
    [InlineData("\"C:\\\\Records\\\\meeting.wav\"", "\" \"", "\"asr-fast\"")]
    [InlineData("\"C:\\\\Records\\\\meeting.wav\"", "\"C:\\\\Transcripts\"", "\" \"")]
    public async Task LoadRejectsBlankRequiredFields(
        string inputFilePath,
        string outputDirectory,
        string asrModelId)
    {
        var path = CreateTempPath();
        await WriteJobJsonAsync(
            path,
            inputFilePath: inputFilePath,
            outputDirectory: outputDirectory,
            asrModelId: asrModelId);
        var repository = new TranscriptionJobRepository(path);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.LoadAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("101")]
    public async Task LoadRejectsInvalidProgressPercent(string progressPercent)
    {
        var path = CreateTempPath();
        await WriteJobJsonAsync(path, progressPercent: progressPercent);
        var repository = new TranscriptionJobRepository(path);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LoadRejectsWhitespaceOutputFileEntries()
    {
        var path = CreateTempPath();
        await WriteJobJsonAsync(path, outputFiles: "[\" \"]");
        var repository = new TranscriptionJobRepository(path);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LoadRejectsNullOutputFileEntries()
    {
        var path = CreateTempPath();
        await WriteJobJsonAsync(path, outputFiles: "[null]");
        var repository = new TranscriptionJobRepository(path);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SaveRejectsNullJobsArray()
    {
        var repository = new TranscriptionJobRepository(CreateTempPath());

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task SaveRejectsNullJobElements()
    {
        var repository = new TranscriptionJobRepository(CreateTempPath());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.SaveAsync([(TranscriptionJob)null!], CancellationToken.None));
    }

    [Fact]
    public async Task SaveRejectsUnknownStatus()
    {
        var repository = new TranscriptionJobRepository(CreateTempPath());
        var job = CreateJob() with { Status = (TranscriptionJobStatus)999 };

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveAsync([job], CancellationToken.None));
    }

    [Fact]
    public async Task SaveRejectsInvalidProgress()
    {
        var repository = new TranscriptionJobRepository(CreateTempPath());
        var job = CreateJob() with { ProgressPercent = 101 };

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveAsync([job], CancellationToken.None));
    }

    [Fact]
    public async Task SaveRejectsNegativeProgress()
    {
        var repository = new TranscriptionJobRepository(CreateTempPath());
        var job = CreateJob() with { ProgressPercent = -1 };

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveAsync([job], CancellationToken.None));
    }

    [Fact]
    public async Task SaveRejectsBlankRequiredPaths()
    {
        var repository = new TranscriptionJobRepository(CreateTempPath());
        var job = CreateJob() with { OutputDirectory = " " };

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveAsync([job], CancellationToken.None));
    }

    [Fact]
    public async Task SaveRejectsBlankInputFilePath()
    {
        var repository = new TranscriptionJobRepository(CreateTempPath());
        var job = CreateJob() with { InputFilePath = " " };

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveAsync([job], CancellationToken.None));
    }

    [Fact]
    public async Task SaveRejectsBlankAsrModelId()
    {
        var repository = new TranscriptionJobRepository(CreateTempPath());
        var job = CreateJob() with { AsrModelId = " " };

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveAsync([job], CancellationToken.None));
    }

    [Fact]
    public async Task SaveRejectsNullOutputFiles()
    {
        var repository = new TranscriptionJobRepository(CreateTempPath());
        var job = CreateJob() with { OutputFiles = null! };

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveAsync([job], CancellationToken.None));
    }

    [Fact]
    public async Task SaveRejectsWhitespaceOutputFileEntries()
    {
        var repository = new TranscriptionJobRepository(CreateTempPath());
        var job = CreateJob() with { OutputFiles = [" "] };

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveAsync([job], CancellationToken.None));
    }

    [Fact]
    public async Task SaveRejectsNullOutputFileEntries()
    {
        var repository = new TranscriptionJobRepository(CreateTempPath());
        var job = CreateJob() with { OutputFiles = [null!] };

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

    private static async Task WriteJobJsonAsync(
        string path,
        string inputFilePath = "\"C:\\\\Records\\\\meeting.wav\"",
        string outputDirectory = "\"C:\\\\Transcripts\"",
        string asrModelId = "\"asr-fast\"",
        string status = "0",
        string progressPercent = "0",
        string outputFiles = "[]")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            $$"""
            [
              {
                "id": "11111111-1111-1111-1111-111111111111",
                "inputFilePath": {{inputFilePath}},
                "outputDirectory": {{outputDirectory}},
                "asrModelId": {{asrModelId}},
                "status": {{status}},
                "progressPercent": {{progressPercent}},
                "createdAt": "2026-05-07T10:00:00+03:00",
                "outputFiles": {{outputFiles}}
              }
            ]
            """);
    }
}
