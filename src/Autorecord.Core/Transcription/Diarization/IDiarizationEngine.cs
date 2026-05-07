using Autorecord.Core.Transcription.Results;

namespace Autorecord.Core.Transcription.Diarization;

public interface IDiarizationEngine
{
    Task<IReadOnlyList<DiarizationTurn>> DiarizeAsync(
        string normalizedWavPath,
        string modelPath,
        int? numSpeakers,
        double? clusterThreshold,
        IProgress<int> progress,
        CancellationToken cancellationToken);
}
