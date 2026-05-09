using Autorecord.Core.Transcription.Pipeline;
using NAudio.Wave;

namespace Autorecord.Core.Transcription.Jobs;

public sealed class TranscriptionQueue
{
    private readonly TranscriptionJobRepository _repository;
    private readonly ITranscriptionPipeline _pipeline;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TranscriptionJobLogWriter? _logWriter;
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
        Func<DateTimeOffset> clock,
        TranscriptionJobLogWriter? logWriter = null)
    {
        _repository = repository;
        _pipeline = pipeline;
        _clock = clock;
        _logWriter = logWriter;
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
            if (_logWriter is not null)
            {
                await TryWriteJobLogAsync(token => _logWriter.WriteStartedAsync(runningJob, token));
            }

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
            catch (TranscriptionModelNotInstalledException ex)
            {
                TranscriptionJob waitingJob = runningJob;
                UpdateJob(runningJob.Id, job =>
                {
                    waitingJob = job with
                    {
                        Status = TranscriptionJobStatus.WaitingForModel,
                        ProgressPercent = 0,
                        StartedAt = null,
                        FinishedAt = null,
                        ErrorMessage = ex.Message
                    };

                    return waitingJob;
                });
                await SaveCurrentAsync(CancellationToken.None);
                if (_logWriter is not null)
                {
                    await TryWriteJobLogAsync(token => _logWriter.WriteFinishedAsync(
                        waitingJob,
                        null,
                        CalculateProcessingTime(waitingJob),
                        token,
                        TryReadInputDurationSec(waitingJob.InputFilePath)));
                }

                OnJobsChanged();
                return;
            }
            catch (OperationCanceledException)
            {
                TranscriptionJob cancelledJob = runningJob;
                UpdateJob(runningJob.Id, job =>
                {
                    cancelledJob = job with
                    {
                        Status = TranscriptionJobStatus.Cancelled,
                        FinishedAt = _clock(),
                        ErrorMessage = "Transcription job was cancelled."
                    };

                    return cancelledJob;
                });
                await SaveCurrentAsync(CancellationToken.None);
                if (_logWriter is not null)
                {
                    await TryWriteJobLogAsync(token => _logWriter.WriteFinishedAsync(
                        cancelledJob,
                        null,
                        CalculateProcessingTime(cancelledJob),
                        token,
                        TryReadInputDurationSec(cancelledJob.InputFilePath)));
                }

                OnJobsChanged();
                return;
            }
            catch (Exception ex)
            {
                TranscriptionJob failedJob = runningJob;
                UpdateJob(runningJob.Id, job =>
                {
                    failedJob = job with
                    {
                        Status = TranscriptionJobStatus.Failed,
                        FinishedAt = _clock(),
                        ErrorMessage = ex.Message
                    };

                    return failedJob;
                });
                await SaveCurrentAsync(CancellationToken.None);
                if (_logWriter is not null)
                {
                    await TryWriteJobLogAsync(token => _logWriter.WriteFinishedAsync(
                        failedJob,
                        null,
                        CalculateProcessingTime(failedJob),
                        token,
                        TryReadInputDurationSec(failedJob.InputFilePath)));
                }

                OnJobsChanged();
                return;
            }
            finally
            {
                ClearRunningCancellation(runningJob.Id);
            }

            TranscriptionJob completedJob = runningJob;
            UpdateJob(runningJob.Id, job =>
            {
                completedJob = job with
                {
                    Status = TranscriptionJobStatus.Completed,
                    ProgressPercent = 100,
                    FinishedAt = _clock(),
                    OutputFiles = result.OutputFiles
                };

                return completedJob;
            });
            await SaveCurrentAsync(CancellationToken.None);
            if (_logWriter is not null)
            {
                await TryWriteJobLogAsync(token => _logWriter.WriteFinishedAsync(
                    completedJob,
                    result,
                    CalculateProcessingTime(completedJob),
                    token));
            }

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

    public async Task<int> RetryWaitingForModelAsync(CancellationToken cancellationToken)
    {
        var updated = 0;
        lock (_jobsGate)
        {
            var jobs = _jobs.ToArray();
            for (var i = 0; i < jobs.Length; i++)
            {
                if (jobs[i].Status != TranscriptionJobStatus.WaitingForModel)
                {
                    continue;
                }

                jobs[i] = jobs[i] with
                {
                    Status = TranscriptionJobStatus.Pending,
                    ProgressPercent = 0,
                    StartedAt = null,
                    FinishedAt = null,
                    ErrorMessage = null
                };
                updated++;
            }

            if (updated > 0)
            {
                _jobs = jobs;
            }
        }

        if (updated == 0)
        {
            return 0;
        }

        await SaveCurrentAsync(cancellationToken);
        OnJobsChanged();
        return updated;
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

    private static TimeSpan CalculateProcessingTime(TranscriptionJob job)
    {
        if (job.StartedAt is null || job.FinishedAt is null)
        {
            return TimeSpan.Zero;
        }

        var processingTime = job.FinishedAt.Value - job.StartedAt.Value;
        return processingTime < TimeSpan.Zero ? TimeSpan.Zero : processingTime;
    }

    private static async Task TryWriteJobLogAsync(Func<CancellationToken, Task> write)
    {
        try
        {
            await write(CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
        }
    }

    private static double? TryReadInputDurationSec(string inputFilePath)
    {
        try
        {
            if (!File.Exists(inputFilePath) ||
                !string.Equals(Path.GetExtension(inputFilePath), ".wav", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            using var reader = new WaveFileReader(inputFilePath);
            return reader.TotalTime.TotalSeconds;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or FormatException)
        {
            return null;
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
