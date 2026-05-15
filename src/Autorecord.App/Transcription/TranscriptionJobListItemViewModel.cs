using Autorecord.Core.Transcription.Jobs;

namespace Autorecord.App.Transcription;

public sealed record TranscriptionJobListItemViewModel
{
    public Guid Id { get; init; }
    public string File { get; init; } = "";
    public string Model { get; init; } = "";
    public string DiarizationModel { get; init; } = "";
    public string Status { get; init; } = "";
    public string StageLines { get; init; } = "";
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
            Model = FormatAsrModel(job.AsrModelId),
            DiarizationModel = string.IsNullOrWhiteSpace(job.DiarizationModelId)
                ? "Без разделения"
                : job.DiarizationModelId,
            Status = FormatJobStatus(job),
            StageLines = FormatStageLines(job),
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

    private static string FormatAsrModel(string modelId)
    {
        return string.Equals(modelId, "gigaam-v3-ru-quality", StringComparison.OrdinalIgnoreCase)
            ? "GigaAM v3"
            : modelId;
    }

    private static string FormatStageLines(TranscriptionJob job)
    {
        var hasDiarization = !string.IsNullOrWhiteSpace(job.DiarizationModelId);
        var currentStage = GetStageIndex(job.ProgressPercent, hasDiarization);
        if (!hasDiarization)
        {
            return string.Join(Environment.NewLine,
                FormatStage("Чтение файла", stageIndex: 0, currentStage, job.Status),
                FormatStage("Транскрибация", stageIndex: 1, currentStage, job.Status),
                FormatStage("Сохранение транскрипта", stageIndex: 2, currentStage, job.Status));
        }

        return string.Join(Environment.NewLine,
            FormatStage("Чтение файла", stageIndex: 0, currentStage, job.Status),
            FormatStage("Диаризация", stageIndex: 1, currentStage, job.Status),
            FormatStage("Транскрибация", stageIndex: 2, currentStage, job.Status),
            FormatStage("Сохранение транскрипта", stageIndex: 3, currentStage, job.Status));
    }

    private static string FormatStage(
        string name,
        int stageIndex,
        int currentStage,
        TranscriptionJobStatus status)
    {
        var state = status switch
        {
            TranscriptionJobStatus.Completed => "готово",
            TranscriptionJobStatus.Pending or TranscriptionJobStatus.WaitingForModel => "ожидает",
            TranscriptionJobStatus.Running => FormatRunningStageState(stageIndex, currentStage),
            TranscriptionJobStatus.Failed => FormatTerminalStageState(stageIndex, currentStage, "ошибка"),
            TranscriptionJobStatus.Cancelled => FormatTerminalStageState(stageIndex, currentStage, "отменено"),
            _ => "ожидает"
        };

        return $"{name}: {state}";
    }

    private static string FormatRunningStageState(int stageIndex, int currentStage)
    {
        if (stageIndex < currentStage)
        {
            return "готово";
        }

        return stageIndex == currentStage ? "выполняется" : "ожидает";
    }

    private static string FormatTerminalStageState(int stageIndex, int currentStage, string currentState)
    {
        if (stageIndex < currentStage)
        {
            return "готово";
        }

        return stageIndex == currentStage ? currentState : "ожидает";
    }

    private static int GetStageIndex(int progressPercent, bool hasDiarization)
    {
        var clamped = Math.Clamp(progressPercent, 0, 100);
        if (!hasDiarization)
        {
            return clamped switch
            {
                < 10 => 0,
                < 95 => 1,
                _ => 2
            };
        }

        return clamped switch
        {
            < 10 => 0,
            < 45 => 1,
            < 95 => 2,
            _ => 3
        };
    }
}
