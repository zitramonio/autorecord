namespace Autorecord.Core.Transcription.Pipeline;

public sealed record TranscriptionPipelineResult(IReadOnlyList<string> OutputFiles, double? DurationSec = null);
