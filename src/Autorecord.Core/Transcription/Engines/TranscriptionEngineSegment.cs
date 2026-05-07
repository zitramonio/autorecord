namespace Autorecord.Core.Transcription.Engines;

public sealed record TranscriptionEngineSegment(double Start, double End, string Text, double? Confidence);
