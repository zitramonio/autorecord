using System.Text.Json;

namespace Autorecord.Core.Transcription.Jobs;

public sealed class TranscriptionJobRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _path;

    public TranscriptionJobRepository(string path)
    {
        _path = path;
    }

    public async Task<IReadOnlyList<TranscriptionJob>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        await using var stream = File.OpenRead(_path);
        var jobs = await JsonSerializer.DeserializeAsync<IReadOnlyList<TranscriptionJob?>>(
            stream,
            JsonOptions,
            cancellationToken);

        if (jobs is null)
        {
            throw new InvalidOperationException("Transcription jobs file must contain a non-null jobs array.");
        }

        Validate(jobs);
        return jobs.Select(job => RestoreInterruptedJob(job!)).ToArray();
    }

    public async Task SaveAsync(IReadOnlyList<TranscriptionJob> jobs, CancellationToken cancellationToken)
    {
        if (jobs is null)
        {
            throw new InvalidOperationException("Transcription jobs array must not be null.");
        }

        Validate(jobs);
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = Path.Combine(
            string.IsNullOrWhiteSpace(directory) ? "." : directory,
            $"{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, jobs, JsonOptions, cancellationToken);
            }

            File.Move(tempPath, _path, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }
    }

    private static TranscriptionJob RestoreInterruptedJob(TranscriptionJob job)
    {
        if (job.Status != TranscriptionJobStatus.Running)
        {
            return job;
        }

        return job with
        {
            Status = TranscriptionJobStatus.Pending,
            StartedAt = null,
            ErrorMessage = "Job was interrupted while running and has been restored to pending."
        };
    }

    private static void Validate(IReadOnlyList<TranscriptionJob?> jobs)
    {
        foreach (var job in jobs)
        {
            if (job is null)
            {
                throw new InvalidOperationException("Transcription jobs array must not contain null jobs.");
            }

            RequireNonBlank(job.InputFilePath, nameof(TranscriptionJob.InputFilePath));
            RequireNonBlank(job.OutputDirectory, nameof(TranscriptionJob.OutputDirectory));
            RequireNonBlank(job.AsrModelId, nameof(TranscriptionJob.AsrModelId));

            if (!Enum.IsDefined(job.Status))
            {
                throw new InvalidOperationException("Transcription job status must be known.");
            }

            if (job.ProgressPercent is < 0 or > 100)
            {
                throw new InvalidOperationException("Transcription job progress must be between 0 and 100.");
            }

            if (job.OutputFiles is null)
            {
                throw new InvalidOperationException("Transcription job output files must not be null.");
            }

            foreach (var outputFile in job.OutputFiles)
            {
                RequireNonBlank(outputFile, nameof(TranscriptionJob.OutputFiles));
            }
        }
    }

    private static void RequireNonBlank(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Transcription job field '{name}' must not be empty.");
        }
    }
}
