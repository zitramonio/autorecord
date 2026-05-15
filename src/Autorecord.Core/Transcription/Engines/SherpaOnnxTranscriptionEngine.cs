using NAudio.Wave;
using SherpaOnnx;

namespace Autorecord.Core.Transcription.Engines;

public sealed class SherpaOnnxTranscriptionEngine : ISegmentedTranscriptionEngine
{
    private const string TokensFileName = "tokens.txt";
    private const string ModelFileName = "model.int8.onnx";
    private const string EncoderFileName = "encoder.int8.onnx";
    private const string DecoderFileName = "decoder.int8.onnx";
    private const string JoinerFileName = "joiner.int8.onnx";
    private const int SampleRate = 16_000;
    private const int FeatureDimension = 80;
    private const int TargetChunkSeconds = 20;
    private const int PaddingMilliseconds = 250;

    private readonly ISherpaOnnxChunkDecoderFactory decoderFactory;

    public SherpaOnnxTranscriptionEngine()
        : this(new SherpaOnnxChunkDecoderFactory())
    {
    }

    internal SherpaOnnxTranscriptionEngine(ISherpaOnnxChunkDecoderFactory decoderFactory)
    {
        this.decoderFactory = decoderFactory;
    }

    public Task<TranscriptionEngineResult> TranscribeAsync(
        string normalizedWavPath,
        string modelPath,
        IProgress<int> progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var modelFiles = ResolveModelFiles(modelPath);

        progress.Report(0);

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var reader = new WaveFileReader(normalizedWavPath);
            using var decoder = decoderFactory.Create(modelFiles);
            var segments = DecodeChunks(reader, decoder, null, progress, cancellationToken);

            return new TranscriptionEngineResult(segments);
        }, cancellationToken);
    }

    public Task<TranscriptionEngineResult> TranscribeAsync(
        string normalizedWavPath,
        string modelPath,
        IReadOnlyList<TranscriptionEngineInterval> intervals,
        IProgress<int> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        cancellationToken.ThrowIfCancellationRequested();

        var modelFiles = ResolveModelFiles(modelPath);

        progress.Report(0);

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var reader = new WaveFileReader(normalizedWavPath);
            using var decoder = decoderFactory.Create(modelFiles);
            var segments = DecodeChunks(reader, decoder, intervals, progress, cancellationToken);

            return new TranscriptionEngineResult(segments);
        }, cancellationToken);
    }

    private static string RequireModelFile(string modelPath, string fileName)
    {
        var path = Path.Combine(modelPath, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Required Sherpa ONNX model file is missing: {fileName}", path);
        }

        return path;
    }

    private static SherpaOnnxModelFiles ResolveModelFiles(string modelPath)
    {
        var tokensPath = RequireModelFile(modelPath, TokensFileName);
        var singleModelPath = Path.Combine(modelPath, ModelFileName);
        if (File.Exists(singleModelPath))
        {
            return SherpaOnnxModelFiles.NeMoCtc(tokensPath, singleModelPath);
        }

        if (!HasAnyTransducerFile(modelPath))
        {
            return SherpaOnnxModelFiles.NeMoCtc(tokensPath, RequireModelFile(modelPath, ModelFileName));
        }

        return SherpaOnnxModelFiles.Transducer(
            tokensPath,
            RequireModelFile(modelPath, EncoderFileName),
            RequireModelFile(modelPath, DecoderFileName),
            RequireModelFile(modelPath, JoinerFileName));
    }

    private static bool HasAnyTransducerFile(string modelPath)
    {
        return File.Exists(Path.Combine(modelPath, EncoderFileName)) ||
            File.Exists(Path.Combine(modelPath, DecoderFileName)) ||
            File.Exists(Path.Combine(modelPath, JoinerFileName));
    }

    private static List<TranscriptionEngineSegment> DecodeChunks(
        WaveFileReader reader,
        ISherpaOnnxChunkDecoder decoder,
        IReadOnlyList<TranscriptionEngineInterval>? intervals,
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

        var chunkSamples = TargetChunkSeconds * SampleRate;
        var paddingSamples = PaddingMilliseconds * SampleRate / 1000;
        var ranges = BuildSampleRanges(intervals, totalSamples);
        if (ranges.Count == 0)
        {
            progress.Report(100);
            return [];
        }

        var totalWorkSamples = ranges.Sum(range => range.EndSample - range.StartSample);
        var completedSamples = 0L;
        var segments = new List<TranscriptionEngineSegment>();

        foreach (var range in ranges)
        {
            for (var chunkStartSample = range.StartSample;
                 chunkStartSample < range.EndSample;
                 chunkStartSample += chunkSamples)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var chunkEndSample = Math.Min(range.EndSample, chunkStartSample + chunkSamples);
                var decodeStartSample = Math.Max(0, chunkStartSample - paddingSamples);
                var decodeEndSample = Math.Min(totalSamples, chunkEndSample + paddingSamples);
                var samples = ReadPcm16MonoSamples(
                    reader,
                    decodeStartSample,
                    checked((int)(decodeEndSample - decodeStartSample)));
                var text = decoder.Decode(samples).Trim();

                cancellationToken.ThrowIfCancellationRequested();

                if (text.Length > 0)
                {
                    segments.Add(new TranscriptionEngineSegment(
                        Start: chunkStartSample / (double)SampleRate,
                        End: chunkEndSample / (double)SampleRate,
                        Text: text,
                        Confidence: null));
                }

                completedSamples += chunkEndSample - chunkStartSample;
                progress.Report((int)(completedSamples * 100 / totalWorkSamples));
            }
        }

        return segments;
    }

    private static IReadOnlyList<SampleRange> BuildSampleRanges(
        IReadOnlyList<TranscriptionEngineInterval>? intervals,
        long totalSamples)
    {
        if (intervals is null)
        {
            return [new SampleRange(0, totalSamples)];
        }

        return intervals
            .Select(interval => ToSampleRange(interval, totalSamples))
            .Where(range => range.EndSample > range.StartSample)
            .OrderBy(range => range.StartSample)
            .ToList();
    }

    private static SampleRange ToSampleRange(TranscriptionEngineInterval interval, long totalSamples)
    {
        if (!double.IsFinite(interval.Start) ||
            !double.IsFinite(interval.End) ||
            interval.Start < 0 ||
            interval.End < interval.Start)
        {
            throw new ArgumentException("Transcription intervals must be finite, non-negative, and ordered.");
        }

        var startSample = Math.Clamp((long)Math.Floor(interval.Start * SampleRate), 0, totalSamples);
        var endSample = Math.Clamp((long)Math.Ceiling(interval.End * SampleRate), 0, totalSamples);
        return new SampleRange(startSample, endSample);
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

    private sealed class SherpaOnnxChunkDecoderFactory : ISherpaOnnxChunkDecoderFactory
    {
        public ISherpaOnnxChunkDecoder Create(SherpaOnnxModelFiles modelFiles)
        {
            return new SherpaOnnxChunkDecoder(modelFiles);
        }
    }

    private sealed class SherpaOnnxChunkDecoder : ISherpaOnnxChunkDecoder
    {
        private readonly OfflineRecognizer recognizer;

        public SherpaOnnxChunkDecoder(SherpaOnnxModelFiles modelFiles)
        {
            var config = new OfflineRecognizerConfig();
            config.FeatConfig.SampleRate = SampleRate;
            config.FeatConfig.FeatureDim = FeatureDimension;
            config.ModelConfig.Tokens = modelFiles.TokensPath;
            if (modelFiles.Kind == SherpaOnnxModelKind.Transducer)
            {
                config.ModelConfig.Transducer.Encoder = modelFiles.EncoderPath;
                config.ModelConfig.Transducer.Decoder = modelFiles.DecoderPath;
                config.ModelConfig.Transducer.Joiner = modelFiles.JoinerPath;
                config.ModelConfig.ModelType = "nemo_transducer";
            }
            else
            {
                config.ModelConfig.NeMoCtc.Model = modelFiles.ModelPath;
            }

            config.ModelConfig.NumThreads = 1;
            config.ModelConfig.Provider = "cpu";
            config.ModelConfig.Debug = 0;
            config.DecodingMethod = "greedy_search";

            recognizer = new OfflineRecognizer(config);
        }

        public string Decode(float[] samples)
        {
            using var stream = recognizer.CreateStream();
            stream.AcceptWaveform(SampleRate, samples);
            recognizer.Decode([stream]);

            return stream.Result.Text;
        }

        public void Dispose()
        {
            recognizer.Dispose();
        }
    }

    private readonly record struct SampleRange(long StartSample, long EndSample);
}

internal interface ISherpaOnnxChunkDecoderFactory
{
    ISherpaOnnxChunkDecoder Create(SherpaOnnxModelFiles modelFiles);
}

internal interface ISherpaOnnxChunkDecoder : IDisposable
{
    string Decode(float[] samples);
}

internal enum SherpaOnnxModelKind
{
    NeMoCtc,
    Transducer
}

internal sealed record SherpaOnnxModelFiles(
    SherpaOnnxModelKind Kind,
    string TokensPath,
    string? ModelPath,
    string? EncoderPath,
    string? DecoderPath,
    string? JoinerPath)
{
    public static SherpaOnnxModelFiles NeMoCtc(string tokensPath, string modelPath) =>
        new(SherpaOnnxModelKind.NeMoCtc, tokensPath, modelPath, null, null, null);

    public static SherpaOnnxModelFiles Transducer(
        string tokensPath,
        string encoderPath,
        string decoderPath,
        string joinerPath) =>
        new(SherpaOnnxModelKind.Transducer, tokensPath, null, encoderPath, decoderPath, joinerPath);
}
