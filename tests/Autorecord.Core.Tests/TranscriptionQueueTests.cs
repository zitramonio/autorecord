using Autorecord.Core.Transcription.Jobs;
using Autorecord.Core.Transcription.Pipeline;

namespace Autorecord.Core.Tests;

public sealed class TranscriptionQueueTests
{
    [Fact]
    public async Task EnqueueAsyncPersistsAndCompletesJob()
    {
        var repository = new TranscriptionJobRepository(CreateTempPath());
        var pipeline = new FakePipeline(
            (_, progress, _) =>
            {
                progress.Report(42);
                return Task.FromResult(new TranscriptionPipelineResult(["C:\\Transcripts\\meeting.md"]));
            });
        var queue = new TranscriptionQueue(repository, pipeline, FixedClock);

        var job = await queue.EnqueueAsync(
            "C:\\Records\\meeting.wav",
            "C:\\Transcripts",
            "asr-fast",
            "diarization",
            CancellationToken.None);
        await queue.RunNextAsync(CancellationToken.None);

        var persisted = await repository.LoadAsync(CancellationToken.None);
        var persistedJob = Assert.Single(persisted);
        Assert.Equal(job.Id, persistedJob.Id);
        Assert.Equal(TranscriptionJobStatus.Completed, persistedJob.Status);
        Assert.Equal(100, persistedJob.ProgressPercent);
        Assert.Equal(["C:\\Transcripts\\meeting.md"], persistedJob.OutputFiles);
    }

    [Fact]
    public async Task JobsChangedFiresWhenJobProgressChanges()
    {
        var repository = new TranscriptionJobRepository(CreateTempPath());
        var pipeline = new FakePipeline(
            (_, progress, _) =>
            {
                progress.Report(42);
                return Task.FromResult(new TranscriptionPipelineResult(["C:\\Transcripts\\meeting.md"]));
            });
        var queue = new TranscriptionQueue(repository, pipeline, FixedClock);
        var snapshots = new List<IReadOnlyList<TranscriptionJob>>();
        queue.JobsChanged += (_, _) => snapshots.Add(queue.Jobs);

        await queue.EnqueueAsync(
            "C:\\Records\\meeting.wav",
            "C:\\Transcripts",
            "asr-fast",
            null,
            CancellationToken.None);
        await queue.RunNextAsync(CancellationToken.None);

        Assert.Contains(snapshots, jobs => jobs.Single().Status == TranscriptionJobStatus.Running);
        Assert.Contains(snapshots, jobs => jobs.Single().ProgressPercent == 42);
        Assert.Contains(snapshots, jobs => jobs.Single().Status == TranscriptionJobStatus.Completed);
    }

    [Fact]
    public async Task InitializeAsyncRestoresPersistedJobs()
    {
        var repository = new TranscriptionJobRepository(CreateTempPath());
        var job = CreateJob();
        await repository.SaveAsync([job], CancellationToken.None);
        var queue = new TranscriptionQueue(repository, new FakePipeline(Succeed), FixedClock);

        await queue.InitializeAsync(CancellationToken.None);

        var loadedJob = Assert.Single(queue.Jobs);
        Assert.Equal(job.Id, loadedJob.Id);
        Assert.Equal(job.InputFilePath, loadedJob.InputFilePath);
    }

    [Fact]
    public async Task RunNextAsyncProcessesOnlyOnePendingJob()
    {
        var repository = new TranscriptionJobRepository(CreateTempPath());
        var pipeline = new FakePipeline(Succeed);
        var queue = new TranscriptionQueue(repository, pipeline, FixedClock);
        var first = await queue.EnqueueAsync(
            "C:\\Records\\first.wav",
            "C:\\Transcripts",
            "asr-fast",
            null,
            CancellationToken.None);
        var second = await queue.EnqueueAsync(
            "C:\\Records\\second.wav",
            "C:\\Transcripts",
            "asr-fast",
            null,
            CancellationToken.None);

        await queue.RunNextAsync(CancellationToken.None);

        var persisted = await repository.LoadAsync(CancellationToken.None);
        Assert.Equal(1, pipeline.RunCount);
        Assert.Equal(TranscriptionJobStatus.Completed, persisted.Single(job => job.Id == first.Id).Status);
        Assert.Equal(TranscriptionJobStatus.Pending, persisted.Single(job => job.Id == second.Id).Status);
    }

    [Fact]
    public async Task EnqueueAsyncCompletesWhilePipelineIsRunning()
    {
        var repository = new TranscriptionJobRepository(CreateTempPath());
        var pipelineStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePipeline = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pipeline = new FakePipeline(async (_, _, _) =>
        {
            pipelineStarted.SetResult();
            await releasePipeline.Task;
            return new TranscriptionPipelineResult(["C:\\Transcripts\\first.md"]);
        });
        var queue = new TranscriptionQueue(repository, pipeline, FixedClock);
        await queue.EnqueueAsync(
            "C:\\Records\\first.wav",
            "C:\\Transcripts",
            "asr-fast",
            null,
            CancellationToken.None);
        var runTask = queue.RunNextAsync(CancellationToken.None);
        await pipelineStarted.Task;

        try
        {
            var enqueueTask = queue.EnqueueAsync(
                "C:\\Records\\second.wav",
                "C:\\Transcripts",
                "asr-fast",
                null,
                CancellationToken.None);
            var completed = await Task.WhenAny(enqueueTask, Task.Delay(TimeSpan.FromMilliseconds(500)));

            Assert.Same(enqueueTask, completed);
            var jobs = await repository.LoadAsync(CancellationToken.None);
            Assert.Contains(jobs, job =>
                job.InputFilePath == "C:\\Records\\second.wav" &&
                job.Status == TranscriptionJobStatus.Pending);
        }
        finally
        {
            releasePipeline.SetResult();
            await runTask;
        }
    }

    [Fact]
    public async Task ParallelRunNextAsyncStartsOnlyOnePipeline()
    {
        var repository = new TranscriptionJobRepository(CreateTempPath());
        var pipelineStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePipeline = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pipeline = new FakePipeline(async (_, _, _) =>
        {
            pipelineStarted.SetResult();
            await releasePipeline.Task;
            return new TranscriptionPipelineResult(["C:\\Transcripts\\meeting.md"]);
        });
        var queue = new TranscriptionQueue(repository, pipeline, FixedClock);
        await queue.EnqueueAsync(
            "C:\\Records\\meeting.wav",
            "C:\\Transcripts",
            "asr-fast",
            null,
            CancellationToken.None);

        var firstRun = queue.RunNextAsync(CancellationToken.None);
        await pipelineStarted.Task;
        var secondRun = queue.RunNextAsync(CancellationToken.None);

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));

            Assert.Equal(1, pipeline.RunCount);
        }
        finally
        {
            releasePipeline.SetResult();
            await Task.WhenAll(firstRun, secondRun);
        }
    }

    [Fact]
    public async Task RunNextAsyncPersistsCompletedWhenTokenCancelsAfterPipelineResult()
    {
        var repository = new TranscriptionJobRepository(CreateTempPath());
        using var cancellation = new CancellationTokenSource();
        var pipeline = new FakePipeline((_, _, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult(new TranscriptionPipelineResult(["C:\\Transcripts\\meeting.md"]));
        });
        var queue = new TranscriptionQueue(repository, pipeline, FixedClock);
        await queue.EnqueueAsync(
            "C:\\Records\\meeting.wav",
            "C:\\Transcripts",
            "asr-fast",
            null,
            CancellationToken.None);

        await queue.RunNextAsync(cancellation.Token);

        var persistedJob = Assert.Single(await repository.LoadAsync(CancellationToken.None));
        Assert.Equal(TranscriptionJobStatus.Completed, persistedJob.Status);
        Assert.Equal(100, persistedJob.ProgressPercent);
        Assert.Equal(["C:\\Transcripts\\meeting.md"], persistedJob.OutputFiles);
    }

    [Fact]
    public async Task RunNextAsyncMarksFailedWhenPipelineThrows()
    {
        var repository = new TranscriptionJobRepository(CreateTempPath());
        var pipeline = new FakePipeline((_, _, _) => throw new InvalidOperationException("pipeline failed"));
        var queue = new TranscriptionQueue(repository, pipeline, FixedClock);
        await queue.EnqueueAsync(
            "C:\\Records\\meeting.wav",
            "C:\\Transcripts",
            "asr-fast",
            null,
            CancellationToken.None);

        await queue.RunNextAsync(CancellationToken.None);

        var persistedJob = Assert.Single(await repository.LoadAsync(CancellationToken.None));
        Assert.Equal(TranscriptionJobStatus.Failed, persistedJob.Status);
        Assert.Equal("pipeline failed", persistedJob.ErrorMessage);
    }

    [Fact]
    public async Task RunNextAsyncMarksCancelledWhenPipelineCancels()
    {
        var repository = new TranscriptionJobRepository(CreateTempPath());
        var pipeline = new FakePipeline((_, _, _) => throw new OperationCanceledException());
        var queue = new TranscriptionQueue(repository, pipeline, FixedClock);
        await queue.EnqueueAsync(
            "C:\\Records\\meeting.wav",
            "C:\\Transcripts",
            "asr-fast",
            null,
            CancellationToken.None);

        await queue.RunNextAsync(CancellationToken.None);

        var persistedJob = Assert.Single(await repository.LoadAsync(CancellationToken.None));
        Assert.Equal(TranscriptionJobStatus.Cancelled, persistedJob.Status);
        Assert.False(string.IsNullOrWhiteSpace(persistedJob.ErrorMessage));
        Assert.Contains("cancel", persistedJob.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnqueueAsyncPersistsSeparateOutputDirectory()
    {
        var repository = new TranscriptionJobRepository(CreateTempPath());
        var queue = new TranscriptionQueue(repository, new FakePipeline(Succeed), FixedClock);

        await queue.EnqueueAsync(
            "C:\\Records\\meeting.wav",
            "D:\\TranscriptOutput",
            "asr-fast",
            null,
            CancellationToken.None);

        var persistedJob = Assert.Single(await repository.LoadAsync(CancellationToken.None));
        Assert.Equal("C:\\Records\\meeting.wav", persistedJob.InputFilePath);
        Assert.Equal("D:\\TranscriptOutput", persistedJob.OutputDirectory);
    }

    [Fact]
    public async Task RetryAsyncResetsTerminalJobToPending()
    {
        var repository = new TranscriptionJobRepository(CreateTempPath());
        var failedJob = CreateJob() with
        {
            Status = TranscriptionJobStatus.Failed,
            ProgressPercent = 67,
            StartedAt = FixedClock(),
            FinishedAt = FixedClock(),
            ErrorMessage = "failed",
            OutputFiles = ["C:\\Transcripts\\meeting.md"]
        };
        await repository.SaveAsync([failedJob], CancellationToken.None);
        var queue = new TranscriptionQueue(repository, new FakePipeline(Succeed), FixedClock);
        await queue.InitializeAsync(CancellationToken.None);

        var retried = await queue.RetryAsync(failedJob.Id, CancellationToken.None);

        var persistedJob = Assert.Single(await repository.LoadAsync(CancellationToken.None));
        Assert.True(retried);
        Assert.Equal(TranscriptionJobStatus.Pending, persistedJob.Status);
        Assert.Equal(0, persistedJob.ProgressPercent);
        Assert.Null(persistedJob.StartedAt);
        Assert.Null(persistedJob.FinishedAt);
        Assert.Null(persistedJob.ErrorMessage);
        Assert.Empty(persistedJob.OutputFiles);
    }

    [Fact]
    public async Task CancelAsyncMarksPendingJobCancelled()
    {
        var repository = new TranscriptionJobRepository(CreateTempPath());
        var queue = new TranscriptionQueue(repository, new FakePipeline(Succeed), FixedClock);
        var job = await queue.EnqueueAsync(
            "C:\\Records\\meeting.wav",
            "C:\\Transcripts",
            "asr-fast",
            null,
            CancellationToken.None);

        var cancelled = await queue.CancelAsync(job.Id, CancellationToken.None);

        var persistedJob = Assert.Single(await repository.LoadAsync(CancellationToken.None));
        Assert.True(cancelled);
        Assert.Equal(TranscriptionJobStatus.Cancelled, persistedJob.Status);
        Assert.Equal(FixedClock(), persistedJob.FinishedAt);
        Assert.Contains("cancel", persistedJob.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancelAsyncCancelsRunningJob()
    {
        var repository = new TranscriptionJobRepository(CreateTempPath());
        var pipelineStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pipeline = new FakePipeline(async (_, _, cancellationToken) =>
        {
            pipelineStarted.SetResult();
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return new TranscriptionPipelineResult(["C:\\Transcripts\\meeting.md"]);
        });
        var queue = new TranscriptionQueue(repository, pipeline, FixedClock);
        var job = await queue.EnqueueAsync(
            "C:\\Records\\meeting.wav",
            "C:\\Transcripts",
            "asr-fast",
            null,
            CancellationToken.None);
        var runTask = queue.RunNextAsync(CancellationToken.None);
        await pipelineStarted.Task;

        var cancelled = await queue.CancelAsync(job.Id, CancellationToken.None);
        await runTask;

        var persistedJob = Assert.Single(await repository.LoadAsync(CancellationToken.None));
        Assert.True(cancelled);
        Assert.Equal(TranscriptionJobStatus.Cancelled, persistedJob.Status);
        Assert.False(string.IsNullOrWhiteSpace(persistedJob.ErrorMessage));
    }

    [Fact]
    public async Task DeleteAsyncRemovesNonRunningJob()
    {
        var repository = new TranscriptionJobRepository(CreateTempPath());
        var queue = new TranscriptionQueue(repository, new FakePipeline(Succeed), FixedClock);
        var job = await queue.EnqueueAsync(
            "C:\\Records\\meeting.wav",
            "C:\\Transcripts",
            "asr-fast",
            null,
            CancellationToken.None);

        var deleted = await queue.DeleteAsync(job.Id, CancellationToken.None);

        Assert.True(deleted);
        Assert.Empty(await repository.LoadAsync(CancellationToken.None));
        Assert.Empty(queue.Jobs);
    }

    [Fact]
    public async Task DeleteAsyncDoesNotCorruptRunningJobWhenEarlierHistoryItemIsRemoved()
    {
        var repository = new TranscriptionJobRepository(CreateTempPath());
        var pipelineStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePipeline = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pipeline = new FakePipeline(async (job, _, _) =>
        {
            if (job.InputFilePath == "C:\\Records\\second.wav")
            {
                pipelineStarted.SetResult();
                await releasePipeline.Task;
            }

            return new TranscriptionPipelineResult(
                [Path.Combine(job.OutputDirectory, $"{Path.GetFileNameWithoutExtension(job.InputFilePath)}.md")]);
        });
        var queue = new TranscriptionQueue(repository, pipeline, FixedClock);
        var completedJob = await queue.EnqueueAsync(
            "C:\\Records\\first.wav",
            "C:\\Transcripts",
            "asr-fast",
            null,
            CancellationToken.None);
        await queue.RunNextAsync(CancellationToken.None);
        var runningJob = await queue.EnqueueAsync(
            "C:\\Records\\second.wav",
            "C:\\Transcripts",
            "asr-fast",
            null,
            CancellationToken.None);
        var runTask = queue.RunNextAsync(CancellationToken.None);
        await pipelineStarted.Task;

        var deleted = await queue.DeleteAsync(completedJob.Id, CancellationToken.None);
        releasePipeline.SetResult();
        await runTask;

        var persistedJob = Assert.Single(await repository.LoadAsync(CancellationToken.None));
        Assert.True(deleted);
        Assert.Equal(runningJob.Id, persistedJob.Id);
        Assert.Equal(TranscriptionJobStatus.Completed, persistedJob.Status);
        Assert.Equal(["C:\\Transcripts\\second.md"], persistedJob.OutputFiles);
    }

    private static DateTimeOffset FixedClock()
    {
        return DateTimeOffset.Parse("2026-05-07T10:00:00+03:00");
    }

    private static string CreateTempPath()
    {
        return Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "jobs.json");
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
            CreatedAt = FixedClock()
        };
    }

    private static Task<TranscriptionPipelineResult> Succeed(
        TranscriptionJob job,
        IProgress<int> progress,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new TranscriptionPipelineResult(
            [Path.Combine(job.OutputDirectory, $"{Path.GetFileNameWithoutExtension(job.InputFilePath)}.md")]));
    }

    private sealed class FakePipeline(
        Func<TranscriptionJob, IProgress<int>, CancellationToken, Task<TranscriptionPipelineResult>> run)
        : ITranscriptionPipeline
    {
        public int RunCount { get; private set; }

        public Task<TranscriptionPipelineResult> RunAsync(
            TranscriptionJob job,
            IProgress<int> progress,
            CancellationToken cancellationToken)
        {
            RunCount++;
            return run(job, progress, cancellationToken);
        }
    }
}
