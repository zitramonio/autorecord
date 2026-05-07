namespace Autorecord.Core.Transcription.Engines;

public interface ITranscriptionEngine
{
    Task<TranscriptionEngineResult> TranscribeAsync(
        string normalizedWavPath,
        string modelPath,
        IProgress<int> progress,
        CancellationToken cancellationToken);
}
