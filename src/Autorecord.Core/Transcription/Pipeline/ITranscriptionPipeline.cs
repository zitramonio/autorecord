using Autorecord.Core.Transcription.Jobs;

namespace Autorecord.Core.Transcription.Pipeline;

public interface ITranscriptionPipeline
{
    Task<TranscriptionPipelineResult> RunAsync(
        TranscriptionJob job,
        IProgress<int> progress,
        CancellationToken cancellationToken);
}
