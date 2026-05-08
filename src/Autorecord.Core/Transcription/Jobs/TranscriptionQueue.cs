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
    private readonly object _runningGate = new();
    private IReadOnlyList<TranscriptionJob> _jobs = [];
    private Guid? _runningJobId;
    private CancellationTokenSource? _runningJobCancellation;

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
        }

        await SaveCurrentAsync(cancellationToken);
        OnJobsChanged();
        return job;
    }

    public async Task RunNextAsync(CancellationToken cancellationToken)
    {
        await _workerGate.WaitAsync(cancellationToken);
        try
        {
            if (!TryStartNextPendingJob(out var runningJob))
            {
                return;
            }

            using var jobCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            SetRunningCancellation(runningJob.Id, jobCancellation);
            await SaveCurrentAsync(cancellationToken);
            OnJobsChanged();

            var progress = new JobProgress(value =>
            {
                UpdateJob(runningJob.Id, job => job with { ProgressPercent = Math.Clamp(value, 0, 100) });
                OnJobsChanged();
            });

            TranscriptionPipelineResult result;
            try
            {
                result = await _pipeline.RunAsync(runningJob, progress, jobCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                UpdateJob(runningJob.Id, job => job with
                {
                    Status = TranscriptionJobStatus.Cancelled,
                    FinishedAt = _clock(),
                    ErrorMessage = "Transcription job was cancelled."
                });
                await SaveCurrentAsync(CancellationToken.None);
                OnJobsChanged();
                return;
            }
            catch (Exception ex)
            {
                UpdateJob(runningJob.Id, job => job with
                {
                    Status = TranscriptionJobStatus.Failed,
                    FinishedAt = _clock(),
                    ErrorMessage = ex.Message
                });
                await SaveCurrentAsync(CancellationToken.None);
                OnJobsChanged();
                return;
            }
            finally
            {
                ClearRunningCancellation(runningJob.Id);
            }

            UpdateJob(runningJob.Id, job => job with
            {
                Status = TranscriptionJobStatus.Completed,
                ProgressPercent = 100,
                FinishedAt = _clock(),
                OutputFiles = result.OutputFiles
            });
            await SaveCurrentAsync(CancellationToken.None);
            OnJobsChanged();
        }
        finally
        {
            _workerGate.Release();
        }
    }

    public async Task<bool> RetryAsync(Guid jobId, CancellationToken cancellationToken)
    {
        lock (_jobsGate)
        {
            var index = FindJobIndexUnderLock(jobId);
            if (index < 0 || _jobs[index].Status == TranscriptionJobStatus.Running)
            {
                return false;
            }

            ReplaceJobUnderLock(index, _jobs[index] with
            {
                Status = TranscriptionJobStatus.Pending,
                ProgressPercent = 0,
                StartedAt = null,
                FinishedAt = null,
                ErrorMessage = null,
                OutputFiles = []
            });
        }

        await SaveCurrentAsync(cancellationToken);
        OnJobsChanged();
        return true;
    }

    public async Task<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken)
    {
        CancellationTokenSource? runningCancellation = null;
        var shouldSave = false;

        lock (_jobsGate)
        {
            var index = FindJobIndexUnderLock(jobId);
            if (index < 0)
            {
                return false;
            }

            var job = _jobs[index];
            if (job.Status == TranscriptionJobStatus.Running)
            {
                lock (_runningGate)
                {
                    if (_runningJobId == jobId)
                    {
                        runningCancellation = _runningJobCancellation;
                    }
                }
            }
            else if (job.Status is TranscriptionJobStatus.Pending or TranscriptionJobStatus.WaitingForModel)
            {
                ReplaceJobUnderLock(index, job with
                {
                    Status = TranscriptionJobStatus.Cancelled,
                    FinishedAt = _clock(),
                    ErrorMessage = "Transcription job was cancelled."
                });
                shouldSave = true;
            }
        }

        if (runningCancellation is not null)
        {
            await runningCancellation.CancelAsync();
            return true;
        }

        if (!shouldSave)
        {
            return false;
        }

        await SaveCurrentAsync(cancellationToken);
        OnJobsChanged();
        return true;
    }

    public async Task<bool> DeleteAsync(Guid jobId, CancellationToken cancellationToken)
    {
        lock (_jobsGate)
        {
            var index = FindJobIndexUnderLock(jobId);
            if (index < 0 || _jobs[index].Status == TranscriptionJobStatus.Running)
            {
                return false;
            }

            _jobs = _jobs.Where(job => job.Id != jobId).ToArray();
        }

        await SaveCurrentAsync(cancellationToken);
        OnJobsChanged();
        return true;
    }

    private bool TryStartNextPendingJob(out TranscriptionJob runningJob)
    {
        lock (_jobsGate)
        {
            runningJob = new TranscriptionJob();

            for (var i = 0; i < _jobs.Count; i++)
            {
                if (_jobs[i].Status == TranscriptionJobStatus.Pending)
                {
                    runningJob = _jobs[i] with
                    {
                        Status = TranscriptionJobStatus.Running,
                        StartedAt = _clock(),
                        ProgressPercent = 0,
                        ErrorMessage = null
                    };
                    ReplaceJobUnderLock(i, runningJob);
                    return true;
                }
            }

            return false;
        }
    }

    private IReadOnlyList<TranscriptionJob> UpdateJob(Guid jobId, Func<TranscriptionJob, TranscriptionJob> update)
    {
        lock (_jobsGate)
        {
            var index = FindJobIndexUnderLock(jobId);
            if (index < 0)
            {
                throw new InvalidOperationException("Transcription job no longer exists.");
            }

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

    private int FindJobIndexUnderLock(Guid jobId)
    {
        for (var i = 0; i < _jobs.Count; i++)
        {
            if (_jobs[i].Id == jobId)
            {
                return i;
            }
        }

        return -1;
    }

    private void SetRunningCancellation(Guid jobId, CancellationTokenSource cancellation)
    {
        lock (_runningGate)
        {
            _runningJobId = jobId;
            _runningJobCancellation = cancellation;
        }
    }

    private void ClearRunningCancellation(Guid jobId)
    {
        lock (_runningGate)
        {
            if (_runningJobId != jobId)
            {
                return;
            }

            _runningJobId = null;
            _runningJobCancellation = null;
        }
    }

    private async Task SaveCurrentAsync(CancellationToken cancellationToken)
    {
        await _repositoryGate.WaitAsync(cancellationToken);
        try
        {
            IReadOnlyList<TranscriptionJob> jobs;
            lock (_jobsGate)
            {
                jobs = _jobs;
            }

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
