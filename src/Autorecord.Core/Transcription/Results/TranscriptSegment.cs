namespace Autorecord.Core.Transcription.Results;

public sealed record TranscriptSegment(
    int Id,
    double Start,
    double End,
    string SpeakerId,
    string SpeakerLabel,
    string Text,
    double? Confidence);
