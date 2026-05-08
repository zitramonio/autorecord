using NAudio.Wave;
using SherpaOnnx;

namespace Autorecord.Core.Transcription.Engines;

public sealed class SherpaOnnxTranscriptionEngine : ITranscriptionEngine
{
    private const string TokensFileName = "tokens.txt";
    private const string ModelFileName = "model.int8.onnx";
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

        var tokensPath = RequireModelFile(modelPath, TokensFileName);
        var onnxModelPath = RequireModelFile(modelPath, ModelFileName);

        progress.Report(0);

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var reader = new WaveFileReader(normalizedWavPath);
            using var decoder = decoderFactory.Create(tokensPath, onnxModelPath);
            var segments = DecodeChunks(reader, decoder, progress, cancellationToken);

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

    private static List<TranscriptionEngineSegment> DecodeChunks(
        WaveFileReader reader,
        ISherpaOnnxChunkDecoder decoder,
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
        var segments = new List<TranscriptionEngineSegment>();

        for (long chunkStartSample = 0; chunkStartSample < totalSamples; chunkStartSample += chunkSamples)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunkEndSample = Math.Min(totalSamples, chunkStartSample + chunkSamples);
            var decodeStartSample = Math.Max(0, chunkStartSample - paddingSamples);
            var decodeEndSample = Math.Min(totalSamples, chunkEndSample + paddingSamples);
            var samples = ReadPcm16MonoSamples(reader, decodeStartSample, checked((int)(decodeEndSample - decodeStartSample)));
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

            progress.Report((int)(chunkEndSample * 100 / totalSamples));
        }

        return segments;
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
        public ISherpaOnnxChunkDecoder Create(string tokensPath, string onnxModelPath)
        {
            return new SherpaOnnxChunkDecoder(tokensPath, onnxModelPath);
        }
    }

    private sealed class SherpaOnnxChunkDecoder : ISherpaOnnxChunkDecoder
    {
        private readonly OfflineRecognizer recognizer;

        public SherpaOnnxChunkDecoder(string tokensPath, string onnxModelPath)
        {
            var config = new OfflineRecognizerConfig();
            config.FeatConfig.SampleRate = SampleRate;
            config.FeatConfig.FeatureDim = FeatureDimension;
            config.ModelConfig.Tokens = tokensPath;
            config.ModelConfig.NeMoCtc.Model = onnxModelPath;
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
}

internal interface ISherpaOnnxChunkDecoderFactory
{
    ISherpaOnnxChunkDecoder Create(string tokensPath, string onnxModelPath);
}

internal interface ISherpaOnnxChunkDecoder : IDisposable
{
    string Decode(float[] samples);
}
