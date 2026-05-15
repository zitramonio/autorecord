namespace Autorecord.Core.Transcription.Engines;

public interface ISegmentedTranscriptionEngine : ITranscriptionEngine
{
    Task<TranscriptionEngineResult> TranscribeAsync(
        string normalizedWavPath,
        string modelPath,
        IReadOnlyList<TranscriptionEngineInterval> intervals,
        IProgress<int> progress,
        CancellationToken cancellationToken);
}
