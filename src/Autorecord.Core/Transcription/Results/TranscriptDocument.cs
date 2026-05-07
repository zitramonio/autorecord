namespace Autorecord.Core.Transcription.Results;

public sealed record TranscriptDocument
{
    public string InputFile { get; init; } = "";
    public double DurationSec { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string AsrModelId { get; init; } = "";
    public string AsrModelDisplayName { get; init; } = "";
    public string? DiarizationModelId { get; init; }
    public string? DiarizationModelDisplayName { get; init; }
    public IReadOnlyList<TranscriptSpeaker> Speakers { get; init; } = [];
    public IReadOnlyList<TranscriptSegment> Segments { get; init; } = [];
    public IReadOnlyList<DiarizationTurn> RawDiarizationSegments { get; init; } = [];
}
