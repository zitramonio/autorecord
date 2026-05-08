using Autorecord.Core.Transcription.Jobs;

namespace Autorecord.App.Transcription;

public sealed record TranscriptionJobListItemViewModel
{
    public Guid Id { get; init; }
    public string File { get; init; } = "";
    public string Model { get; init; } = "";
    public string Status { get; init; } = "";
    public string Progress { get; init; } = "";
    public string CreatedAt { get; init; } = "";
    public string CompletedAt { get; init; } = "";
    public bool CanOpenTranscript { get; init; }
    public bool CanOpenFolder { get; init; }
    public bool CanRetry { get; init; }
    public bool CanCancel { get; init; }
    public bool CanDelete { get; init; }

    public static TranscriptionJobListItemViewModel FromJob(TranscriptionJob job)
    {
        return new TranscriptionJobListItemViewModel
        {
            Id = job.Id,
            File = job.InputFilePath,
            Model = job.AsrModelId,
            Status = FormatJobStatus(job),
            Progress = $"{job.ProgressPercent}%",
            CreatedAt = job.CreatedAt.ToLocalTime().ToString("g"),
            CompletedAt = job.FinishedAt?.ToLocalTime().ToString("g") ?? "",
            CanOpenTranscript = job.Status == TranscriptionJobStatus.Completed && job.OutputFiles.Count > 0,
            CanOpenFolder = !string.IsNullOrWhiteSpace(job.OutputDirectory),
            CanRetry = job.Status is TranscriptionJobStatus.Completed
                or TranscriptionJobStatus.Failed
                or TranscriptionJobStatus.Cancelled
                or TranscriptionJobStatus.WaitingForModel,
            CanCancel = job.Status is TranscriptionJobStatus.Pending
                or TranscriptionJobStatus.WaitingForModel
                or TranscriptionJobStatus.Running,
            CanDelete = job.Status != TranscriptionJobStatus.Running
        };
    }

    private static string FormatJobStatus(TranscriptionJob job)
    {
        return job.Status == TranscriptionJobStatus.Failed && !string.IsNullOrWhiteSpace(job.ErrorMessage)
            ? $"{job.Status}: {job.ErrorMessage}"
            : job.Status.ToString();
    }
}
