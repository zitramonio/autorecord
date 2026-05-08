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
    private const int SampleRate = 16_000;
    private const int DefaultChunkSeconds = 30 * 60;
    private const int DefaultPaddingSeconds = 5;

    private readonly IDiarizationDecoderFactory decoderFactory;
    private readonly DiarizationChunkOptions chunkOptions;

    public DiarizationEngine()
        : this(new DiarizationDecoderFactory(), new DiarizationChunkOptions(DefaultChunkSeconds, DefaultPaddingSeconds))
    {
    }

    internal DiarizationEngine(IDiarizationDecoderFactory decoderFactory, DiarizationChunkOptions chunkOptions)
    {
        ArgumentNullException.ThrowIfNull(decoderFactory);
        if (chunkOptions.ChunkSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkOptions), chunkOptions, "Chunk length must be positive.");
        }

        if (chunkOptions.PaddingSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkOptions), chunkOptions, "Chunk padding cannot be negative.");
        }

        this.decoderFactory = decoderFactory;
        this.chunkOptions = chunkOptions;
    }

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

            using var decoder = decoderFactory.Create(segmentationModelPath, embeddingModelPath, numSpeakers, clusterThreshold);
            using var reader = new WaveFileReader(normalizedWavPath);
            var normalized = DecodeChunksAndNormalize(reader, decoder, progress, cancellationToken);

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

    private IReadOnlyList<DiarizationTurn> DecodeChunksAndNormalize(
        WaveFileReader reader,
        IDiarizationDecoder decoder,
        IProgress<int> progress,
        CancellationToken cancellationToken)
    {
        const int bytesPerSample = sizeof(short);
        var totalSamples = reader.Length / bytesPerSample;
        if (totalSamples == 0)
        {
            progress.Report(100);
            return [];
        }

        var chunkSamples = checked(chunkOptions.ChunkSeconds * SampleRate);
        var paddingSamples = checked(chunkOptions.PaddingSeconds * SampleRate);
        var turns = new List<DiarizationTurn>();
        var previousPaddedTurns = new List<DiarizationTurn>();
        var nextGlobalSpeakerIndex = 0;

        for (long chunkStartSample = 0; chunkStartSample < totalSamples; chunkStartSample += chunkSamples)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunkEndSample = Math.Min(totalSamples, chunkStartSample + chunkSamples);
            var decodeStartSample = Math.Max(0, chunkStartSample - paddingSamples);
            var decodeEndSample = Math.Min(totalSamples, chunkEndSample + paddingSamples);
            var samples = ReadPcm16MonoSamples(reader, decodeStartSample, checked((int)(decodeEndSample - decodeStartSample)));
            var decodedTurns = decoder.Decode(samples);

            cancellationToken.ThrowIfCancellationRequested();

            var decodeStartSeconds = decodeStartSample / (double)SampleRate;
            var chunkStartSeconds = chunkStartSample / (double)SampleRate;
            var chunkEndSeconds = chunkEndSample / (double)SampleRate;
            var globalLocalTurns = decodedTurns
                .Select(turn => ToGlobalTurn(turn, decodeStartSeconds))
                .ToList();
            var speakerMap = BuildSpeakerMap(globalLocalTurns, previousPaddedTurns, ref nextGlobalSpeakerIndex);
            var paddedTurns = new List<DiarizationTurn>();

            foreach (var turn in globalLocalTurns)
            {
                paddedTurns.Add(turn with { SpeakerId = speakerMap[turn.SpeakerId] });
            }

            foreach (var turn in decodedTurns)
            {
                var globalTurn = ToClippedGlobalTurn(
                    turn,
                    decodeStartSeconds,
                    chunkStartSeconds,
                    chunkEndSeconds,
                    speakerMap);

                if (globalTurn is not null)
                {
                    turns.Add(globalTurn);
                }
            }

            previousPaddedTurns = paddedTurns;
            progress.Report((int)(chunkEndSample * 100 / totalSamples));
        }

        return NormalizeTurns(turns);
    }

    internal static string FormatSpeakerId(int speaker)
    {
        return $"SPEAKER_{speaker.ToString("00", CultureInfo.InvariantCulture)}";
    }

    private static Dictionary<string, string> BuildSpeakerMap(
        IReadOnlyList<DiarizationTurn> currentTurns,
        IReadOnlyList<DiarizationTurn> previousTurns,
        ref int nextGlobalSpeakerIndex)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var usedGlobalSpeakerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matches = currentTurns
            .SelectMany(current => previousTurns.Select(previous => new
            {
                LocalSpeakerId = current.SpeakerId,
                GlobalSpeakerId = previous.SpeakerId,
                Overlap = Math.Min(current.End, previous.End) - Math.Max(current.Start, previous.Start),
            }))
            .Where(match => match.Overlap > 0)
            .GroupBy(match => new { match.LocalSpeakerId, match.GlobalSpeakerId })
            .Select(group => new
            {
                group.Key.LocalSpeakerId,
                group.Key.GlobalSpeakerId,
                Overlap = group.Sum(match => match.Overlap),
            })
            .OrderByDescending(match => match.Overlap);

        foreach (var match in matches)
        {
            if (map.ContainsKey(match.LocalSpeakerId) || !usedGlobalSpeakerIds.Add(match.GlobalSpeakerId))
            {
                continue;
            }

            map[match.LocalSpeakerId] = match.GlobalSpeakerId;
        }

        foreach (var localSpeakerId in currentTurns.Select(turn => turn.SpeakerId).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!map.ContainsKey(localSpeakerId))
            {
                map[localSpeakerId] = FormatSpeakerId(nextGlobalSpeakerIndex++);
            }
        }

        return map;
    }

    private static DiarizationTurn ToGlobalTurn(DiarizationTurn turn, double decodeStartSeconds)
    {
        ValidateTurn(turn);

        return new DiarizationTurn(
            decodeStartSeconds + turn.Start,
            decodeStartSeconds + turn.End,
            turn.SpeakerId);
    }

    private static DiarizationTurn? ToClippedGlobalTurn(
        DiarizationTurn turn,
        double decodeStartSeconds,
        double chunkStartSeconds,
        double chunkEndSeconds,
        IReadOnlyDictionary<string, string> speakerMap)
    {
        ValidateTurn(turn);

        var start = Math.Max(decodeStartSeconds + turn.Start, chunkStartSeconds);
        var end = Math.Min(decodeStartSeconds + turn.End, chunkEndSeconds);
        if (end <= start)
        {
            return null;
        }

        return new DiarizationTurn(start, end, speakerMap[turn.SpeakerId]);
    }

    private static float[] ReadPcm16MonoSamples(WaveFileReader reader, long startSample, int sampleCount)
    {
        const int bytesPerSample = sizeof(short);
        var bytes = new byte[sampleCount * bytesPerSample];
        reader.Position = startSample * bytesPerSample;

        var bytesRead = 0;
        while (bytesRead < bytes.Length)
        {
            var read = reader.Read(bytes, bytesRead, bytes.Length - bytesRead);
            if (read == 0)
            {
                break;
            }

            bytesRead += read;
        }

        var samples = new float[bytesRead / bytesPerSample];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = BitConverter.ToInt16(bytes, i * bytesPerSample) / 32768f;
        }

        return samples;
    }

    private sealed class DiarizationDecoderFactory : IDiarizationDecoderFactory
    {
        public IDiarizationDecoder Create(
            string segmentationModelPath,
            string embeddingModelPath,
            int? numSpeakers,
            double? clusterThreshold)
        {
            return new DiarizationDecoder(segmentationModelPath, embeddingModelPath, numSpeakers, clusterThreshold);
        }
    }

    private sealed class DiarizationDecoder : IDiarizationDecoder
    {
        private readonly OfflineSpeakerDiarization diarization;

        public DiarizationDecoder(
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

            diarization = new OfflineSpeakerDiarization(config);
        }

        public IReadOnlyList<DiarizationTurn> Decode(float[] samples)
        {
            return diarization
                .Process(samples)
                .Select(segment => new DiarizationTurn(segment.Start, segment.End, FormatSpeakerId(segment.Speaker)))
                .ToArray();
        }

        public void Dispose()
        {
            diarization.Dispose();
        }
    }
}

internal readonly record struct DiarizationChunkOptions(int ChunkSeconds, int PaddingSeconds);

internal interface IDiarizationDecoderFactory
{
    IDiarizationDecoder Create(
        string segmentationModelPath,
        string embeddingModelPath,
        int? numSpeakers,
        double? clusterThreshold);
}

internal interface IDiarizationDecoder : IDisposable
{
    IReadOnlyList<DiarizationTurn> Decode(float[] samples);
}
