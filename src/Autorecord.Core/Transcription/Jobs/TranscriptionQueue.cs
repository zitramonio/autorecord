using Autorecord.Core.Transcription.Pipeline;

namespace Autorecord.Core.Transcription.Jobs;

public sealed class TranscriptionQueue
{
    private readonly TranscriptionJobRepository _repository;
    private readonly ITranscriptionPipeline _pipeline;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _jobsGate = new();
    private readonly SemaphoreSlim _repositoryGate = new(1, 1);
    private readonly SemaphoreSlim _workerGate = new(1, 1);
    private IReadOnlyList<TranscriptionJob> _jobs = [];

    public TranscriptionQueue(
        TranscriptionJobRepository repository,
        ITranscriptionPipeline pipeline,
        Func<DateTimeOffset> clock)
    {
        _repository = repository;
        _pipeline = pipeline;
        _clock = clock;
    }

    public event EventHandler? JobsChanged;

    public IReadOnlyList<TranscriptionJob> Jobs
    {
        get
        {
            lock (_jobsGate)
            {
                return _jobs;
            }
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var jobs = await _repository.LoadAsync(cancellationToken);
        lock (_jobsGate)
        {
            _jobs = jobs;
        }

        OnJobsChanged();
    }

    public async Task<TranscriptionJob> EnqueueAsync(
        string inputFilePath,
        string outputDirectory,
        string asrModelId,
        string? diarizationModelId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TranscriptionJob> jobs;
        var job = new TranscriptionJob
        {
            Id = Guid.NewGuid(),
            InputFilePath = inputFilePath,
            OutputDirectory = outputDirectory,
            AsrModelId = asrModelId,
            DiarizationModelId = diarizationModelId,
            Status = TranscriptionJobStatus.Pending,
            ProgressPercent = 0,
            CreatedAt = _clock()
        };

        lock (_jobsGate)
        {
            _jobs = _jobs.Append(job).ToArray();
            jobs = _jobs;
        }

        await SaveAsync(jobs, cancellationToken);
        OnJobsChanged();
        return job;
    }

    public async Task RunNextAsync(CancellationToken cancellationToken)
    {
        await _workerGate.WaitAsync(cancellationToken);
        try
        {
            if (!TryStartNextPendingJob(out var index, out var runningJob, out var jobs))
            {
                return;
            }

            await SaveAsync(jobs, cancellationToken);
            OnJobsChanged();

            var progress = new JobProgress(value =>
            {
                UpdateJob(index, job => job with { ProgressPercent = Math.Clamp(value, 0, 100) });
                OnJobsChanged();
            });

            TranscriptionPipelineResult result;
            try
            {
                result = await _pipeline.RunAsync(runningJob, progress, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                jobs = UpdateJob(index, job => job with
                {
                    Status = TranscriptionJobStatus.Cancelled,
                    FinishedAt = _clock(),
                    ErrorMessage = "Transcription job was cancelled."
                });
                await SaveAsync(jobs, CancellationToken.None);
                OnJobsChanged();
                return;
            }
            catch (Exception ex)
            {
                jobs = UpdateJob(index, job => job with
                {
                    Status = TranscriptionJobStatus.Failed,
                    FinishedAt = _clock(),
                    ErrorMessage = ex.Message
                });
                await SaveAsync(jobs, CancellationToken.None);
                OnJobsChanged();
                return;
            }

            jobs = UpdateJob(index, job => job with
            {
                Status = TranscriptionJobStatus.Completed,
                ProgressPercent = 100,
                FinishedAt = _clock(),
                OutputFiles = result.OutputFiles
            });
            await SaveAsync(jobs, CancellationToken.None);
            OnJobsChanged();
        }
        finally
        {
            _workerGate.Release();
        }
    }

    private bool TryStartNextPendingJob(
        out int index,
        out TranscriptionJob runningJob,
        out IReadOnlyList<TranscriptionJob> jobs)
    {
        lock (_jobsGate)
        {
            index = -1;
            runningJob = new TranscriptionJob();
            jobs = _jobs;

            for (var i = 0; i < _jobs.Count; i++)
            {
                if (_jobs[i].Status == TranscriptionJobStatus.Pending)
                {
                    index = i;
                    runningJob = _jobs[i] with
                    {
                        Status = TranscriptionJobStatus.Running,
                        StartedAt = _clock(),
                        ProgressPercent = 0,
                        ErrorMessage = null
                    };
                    jobs = ReplaceJobUnderLock(index, runningJob);
                    return true;
                }
            }

            return false;
        }
    }

    private IReadOnlyList<TranscriptionJob> UpdateJob(int index, Func<TranscriptionJob, TranscriptionJob> update)
    {
        lock (_jobsGate)
        {
            return ReplaceJobUnderLock(index, update(_jobs[index]));
        }
    }

    private IReadOnlyList<TranscriptionJob> ReplaceJobUnderLock(int index, TranscriptionJob job)
    {
        var jobs = _jobs.ToArray();
        jobs[index] = job;
        _jobs = jobs;
        return jobs;
    }

    private async Task SaveAsync(IReadOnlyList<TranscriptionJob> jobs, CancellationToken cancellationToken)
    {
        await _repositoryGate.WaitAsync(cancellationToken);
        try
        {
            await _repository.SaveAsync(jobs, cancellationToken);
        }
        finally
        {
            _repositoryGate.Release();
        }
    }

    private void OnJobsChanged()
    {
        JobsChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class JobProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value)
        {
            report(value);
        }
    }
}
