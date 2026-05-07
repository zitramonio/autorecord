using Autorecord.Core.Transcription.Pipeline;

namespace Autorecord.Core.Transcription.Jobs;

public sealed class TranscriptionQueue
{
    private readonly TranscriptionJobRepository _repository;
    private readonly ITranscriptionPipeline _pipeline;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
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

    public IReadOnlyList<TranscriptionJob> Jobs => _jobs;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _jobs = await _repository.LoadAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TranscriptionJob> EnqueueAsync(
        string inputFilePath,
        string outputDirectory,
        string asrModelId,
        string? diarizationModelId,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
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

            _jobs = _jobs.Append(job).ToArray();
            await _repository.SaveAsync(_jobs, cancellationToken);
            return job;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RunNextAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var index = FindNextPendingJobIndex();
            if (index < 0)
            {
                return;
            }

            var runningJob = _jobs[index] with
            {
                Status = TranscriptionJobStatus.Running,
                StartedAt = _clock(),
                ProgressPercent = 0,
                ErrorMessage = null
            };
            ReplaceJob(index, runningJob);
            await _repository.SaveAsync(_jobs, cancellationToken);

            var progress = new JobProgress(value =>
                ReplaceJob(index, _jobs[index] with { ProgressPercent = Math.Clamp(value, 0, 100) }));

            TranscriptionPipelineResult result;
            try
            {
                result = await _pipeline.RunAsync(runningJob, progress, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ReplaceJob(index, _jobs[index] with
                {
                    Status = TranscriptionJobStatus.Cancelled,
                    FinishedAt = _clock(),
                    ErrorMessage = "Transcription job was cancelled."
                });
                await _repository.SaveAsync(_jobs, CancellationToken.None);
                return;
            }
            catch (Exception ex)
            {
                ReplaceJob(index, _jobs[index] with
                {
                    Status = TranscriptionJobStatus.Failed,
                    FinishedAt = _clock(),
                    ErrorMessage = ex.Message
                });
                await _repository.SaveAsync(_jobs, CancellationToken.None);
                return;
            }

            ReplaceJob(index, _jobs[index] with
            {
                Status = TranscriptionJobStatus.Completed,
                ProgressPercent = 100,
                FinishedAt = _clock(),
                OutputFiles = result.OutputFiles
            });
            await _repository.SaveAsync(_jobs, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private int FindNextPendingJobIndex()
    {
        for (var i = 0; i < _jobs.Count; i++)
        {
            if (_jobs[i].Status == TranscriptionJobStatus.Pending)
            {
                return i;
            }
        }

        return -1;
    }

    private void ReplaceJob(int index, TranscriptionJob job)
    {
        var jobs = _jobs.ToArray();
        jobs[index] = job;
        _jobs = jobs;
    }

    private sealed class JobProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value)
        {
            report(value);
        }
    }
}
