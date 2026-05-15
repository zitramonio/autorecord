using Autorecord.Core.Transcription.Results;

namespace Autorecord.Core.Transcription.Diarization;

public sealed class PyannoteCommunityDiarizationEngine : IDiarizationEngine
{
    private const string ConfigFileName = "config.yaml";

    private readonly string _workerPath;
    private readonly PyannoteCommunityWorkerClient _client;

    public PyannoteCommunityDiarizationEngine(string workerPath, PyannoteCommunityWorkerClient client)
    {
        _workerPath = workerPath;
        _client = client;
    }

    public async Task<IReadOnlyList<DiarizationTurn>> DiarizeAsync(
        string normalizedWavPath,
        string modelPath,
        int? numSpeakers,
        double? clusterThreshold,
        IProgress<int> progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (numSpeakers is < 1 or > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(numSpeakers), numSpeakers, "Speaker count must be null or between 1 and 6.");
        }

        if (clusterThreshold is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(clusterThreshold), clusterThreshold, "Cluster threshold must be null or greater than 0 and less than or equal to 1.");
        }

        if (!File.Exists(_workerPath))
        {
            throw new FileNotFoundException("Pyannote Community-1 worker is not installed.", _workerPath);
        }

        if (!Directory.Exists(modelPath))
        {
            throw new DirectoryNotFoundException($"Pyannote Community-1 model folder is not installed: {modelPath}");
        }

        var configPath = Path.Combine(modelPath, ConfigFileName);
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"Required Pyannote Community-1 model file is missing: {ConfigFileName}", configPath);
        }

        var outputJsonPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        try
        {
            progress.Report(0);
            var turns = await _client.RunAsync(
                _workerPath,
                normalizedWavPath,
                modelPath,
                outputJsonPath,
                numSpeakers,
                clusterThreshold,
                cancellationToken);
            return DiarizationEngine.NormalizeTurnsAndReportCompletion(turns, progress);
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
