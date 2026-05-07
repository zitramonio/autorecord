namespace Autorecord.Core.Transcription.Engines;

public sealed record TranscriptionEngineResult(IReadOnlyList<TranscriptionEngineSegment> Segments);
