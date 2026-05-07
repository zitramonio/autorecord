namespace Autorecord.Core.Transcription.Jobs;

public sealed record TranscriptionJob
{
    public Guid Id { get; init; }
    public string InputFilePath { get; init; } = "";
    public string OutputDirectory { get; init; } = "";
    public string AsrModelId { get; init; } = "";
    public string? DiarizationModelId { get; init; }
    public TranscriptionJobStatus Status { get; init; }
    public int ProgressPercent { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<string> OutputFiles { get; init; } = [];
}
