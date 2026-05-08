namespace Autorecord.Core.Transcription.Engines;

public sealed class GigaAmV3TranscriptionEngine : ITranscriptionEngine
{
    private readonly string _workerPath;
    private readonly GigaAmWorkerClient _client;

    public GigaAmV3TranscriptionEngine(string workerPath, GigaAmWorkerClient client)
    {
        _workerPath = workerPath;
        _client = client;
    }

    public async Task<TranscriptionEngineResult> TranscribeAsync(
        string normalizedWavPath,
        string modelPath,
        IProgress<int> progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_workerPath))
        {
            throw new FileNotFoundException("GigaAM worker is not installed.", _workerPath);
        }

        if (!Directory.Exists(modelPath))
        {
            throw new DirectoryNotFoundException($"GigaAM model folder is not installed: {modelPath}");
        }

        var outputJsonPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        try
        {
            progress.Report(0);
            var result = await _client.RunAsync(_workerPath, normalizedWavPath, modelPath, outputJsonPath, cancellationToken);
            progress.Report(100);
            return result;
        }
        finally
        {
            if (File.Exists(outputJsonPath))
            {
                File.Delete(outputJsonPath);
            }
        }
    }
}
