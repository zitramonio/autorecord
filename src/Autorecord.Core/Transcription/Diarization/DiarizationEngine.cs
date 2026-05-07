using Autorecord.Core.Transcription.Results;
using System.Globalization;
using NAudio.Wave;
using SherpaOnnx;

namespace Autorecord.Core.Transcription.Diarization;

public sealed class DiarizationEngine : IDiarizationEngine
{
    private const string QualitySegmentationModelFileName = "model.onnx";
    private const string FastSegmentationModelFileName = "model.int8.onnx";
    private const string EmbeddingModelFileName = "3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx";
    private const double MinimumTurnDurationSeconds = 0.25;
    private const double MergeGapSeconds = 0.7;

    public Task<IReadOnlyList<DiarizationTurn>> DiarizeAsync(
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

        var segmentationModelPath = RequireSegmentationModelFile(modelPath);
        var embeddingModelPath = RequireModelFile(modelPath, EmbeddingModelFileName);

        progress.Report(0);

        return Task.Run<IReadOnlyList<DiarizationTurn>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var turns = Decode(normalizedWavPath, segmentationModelPath, embeddingModelPath, numSpeakers, clusterThreshold);

            cancellationToken.ThrowIfCancellationRequested();

            var normalized = NormalizeTurnsAndReportCompletion(turns, progress);

            return normalized;
        }, cancellationToken);
    }

    public static IReadOnlyList<DiarizationTurn> NormalizeTurns(IEnumerable<DiarizationTurn> turns)
    {
        ArgumentNullException.ThrowIfNull(turns);

        var normalized = new List<DiarizationTurn>();
        var validTurns = new List<DiarizationTurn>();

        foreach (var turn in turns)
        {
            ValidateTurn(turn);

            if (turn.End - turn.Start >= MinimumTurnDurationSeconds)
            {
                validTurns.Add(turn);
            }
        }

        foreach (var turn in validTurns.OrderBy(turn => turn.Start))
        {
            if (normalized.Count == 0)
            {
                normalized.Add(turn);
                continue;
            }

            var previous = normalized[^1];
            var gap = turn.Start - previous.End;

            if (previous.SpeakerId == turn.SpeakerId && gap <= MergeGapSeconds)
            {
                normalized[^1] = previous with { End = Math.Max(previous.End, turn.End) };
                continue;
            }

            normalized.Add(turn);
        }

        return normalized;
    }

    internal static IReadOnlyList<DiarizationTurn> NormalizeTurnsAndReportCompletion(
        IEnumerable<DiarizationTurn> turns,
        IProgress<int> progress)
    {
        var normalized = NormalizeTurns(turns);

        progress.Report(100);

        return normalized;
    }

    private static void ValidateTurn(DiarizationTurn? turn)
    {
        if (turn is null)
        {
            throw new ArgumentException("Diarization turn cannot be null.", nameof(turn));
        }

        if (double.IsNaN(turn.Start) || double.IsInfinity(turn.Start))
        {
            throw new ArgumentException("Diarization turn start must be a finite timestamp.", nameof(turn));
        }

        if (double.IsNaN(turn.End) || double.IsInfinity(turn.End))
        {
            throw new ArgumentException("Diarization turn end must be a finite timestamp.", nameof(turn));
        }

        if (turn.Start < 0 || turn.End < 0)
        {
            throw new ArgumentException("Diarization turn timestamps cannot be negative.", nameof(turn));
        }

        if (turn.End < turn.Start)
        {
            throw new ArgumentException("Diarization turn end cannot be before start.", nameof(turn));
        }

        if (string.IsNullOrWhiteSpace(turn.SpeakerId))
        {
            throw new ArgumentException("Diarization turn speaker id cannot be blank.", nameof(turn));
        }
    }

    private static string RequireSegmentationModelFile(string modelPath)
    {
        var qualityModelPath = Path.Combine(modelPath, QualitySegmentationModelFileName);
        if (File.Exists(qualityModelPath))
        {
            return qualityModelPath;
        }

        var fastModelPath = Path.Combine(modelPath, FastSegmentationModelFileName);
        if (File.Exists(fastModelPath))
        {
            return fastModelPath;
        }

        throw new FileNotFoundException(
            $"Required Sherpa ONNX diarization segmentation model file is missing: {QualitySegmentationModelFileName} or {FastSegmentationModelFileName}",
            qualityModelPath);
    }

    private static string RequireModelFile(string modelPath, string fileName)
    {
        var path = Path.Combine(modelPath, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Required Sherpa ONNX diarization model file is missing: {fileName}", path);
        }

        return path;
    }

    private static IReadOnlyList<DiarizationTurn> Decode(
        string normalizedWavPath,
        string segmentationModelPath,
        string embeddingModelPath,
        int? numSpeakers,
        double? clusterThreshold)
    {
        var config = new OfflineSpeakerDiarizationConfig();
        config.Segmentation.Pyannote.Model = segmentationModelPath;
        config.Segmentation.NumThreads = 1;
        config.Segmentation.Provider = "cpu";
        config.Segmentation.Debug = 0;
        config.Embedding.Model = embeddingModelPath;
        config.Embedding.NumThreads = 1;
        config.Embedding.Provider = "cpu";
        config.Embedding.Debug = 0;
        config.Clustering.NumClusters = numSpeakers ?? -1;

        if (clusterThreshold.HasValue)
        {
            config.Clustering.Threshold = (float)clusterThreshold.Value;
        }

        using var diarization = new OfflineSpeakerDiarization(config);
        var samples = ReadPcm16MonoSamples(normalizedWavPath);

        return diarization
            .Process(samples)
            .Select(segment => new DiarizationTurn(segment.Start, segment.End, FormatSpeakerId(segment.Speaker)))
            .ToArray();
    }

    internal static string FormatSpeakerId(int speaker)
    {
        return $"SPEAKER_{speaker.ToString("00", CultureInfo.InvariantCulture)}";
    }

    private static float[] ReadPcm16MonoSamples(string normalizedWavPath)
    {
        using var reader = new WaveFileReader(normalizedWavPath);
        var bytes = new byte[reader.Length];
        var bytesRead = reader.Read(bytes, 0, bytes.Length);
        var samples = new float[bytesRead / 2];

        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = BitConverter.ToInt16(bytes, i * 2) / 32768f;
        }

        return samples;
    }
}
