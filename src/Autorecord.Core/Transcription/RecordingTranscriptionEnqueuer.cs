using Autorecord.Core.Recording;
using Autorecord.Core.Settings;

namespace Autorecord.Core.Transcription;

public static class RecordingTranscriptionEnqueuer
{
    public delegate Task EnqueueTranscriptionJob(
        string inputFilePath,
        string outputDirectory,
        string asrModelId,
        string? diarizationModelId,
        CancellationToken cancellationToken);

    public static async Task<bool> EnqueueAsync(
        RecordingSession session,
        TranscriptionSettings settings,
        Func<string, TranscriptionSettings, string> resolveOutputDirectory,
        EnqueueTranscriptionJob enqueue,
        CancellationToken cancellationToken)
    {
        if (!settings.AutoTranscribeAfterRecording)
        {
            return false;
        }

        var outputDirectory = resolveOutputDirectory(session.OutputPath, settings);
        var diarizationModelId = settings.EnableDiarization
            ? settings.SelectedDiarizationModelId
            : null;

        await enqueue(
            session.OutputPath,
            outputDirectory,
            settings.SelectedAsrModelId,
            diarizationModelId,
            cancellationToken);
        return true;
    }
}
