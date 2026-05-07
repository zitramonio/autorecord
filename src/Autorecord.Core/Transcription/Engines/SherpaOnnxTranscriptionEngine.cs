using NAudio.Wave;
using SherpaOnnx;

namespace Autorecord.Core.Transcription.Engines;

public sealed class SherpaOnnxTranscriptionEngine : ITranscriptionEngine
{
    private const string TokensFileName = "tokens.txt";
    private const string ModelFileName = "model.int8.onnx";
    private const int SampleRate = 16_000;
    private const int FeatureDimension = 80;

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

            var duration = GetWavDuration(normalizedWavPath);
            var text = Decode(normalizedWavPath, tokensPath, onnxModelPath);

            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(100);

            return new TranscriptionEngineResult(
                [new TranscriptionEngineSegment(0, duration, text, Confidence: null)]);
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

    private static double GetWavDuration(string normalizedWavPath)
    {
        using var reader = new WaveFileReader(normalizedWavPath);
        return reader.TotalTime.TotalSeconds;
    }

    private static string Decode(string normalizedWavPath, string tokensPath, string onnxModelPath)
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

        using var recognizer = new OfflineRecognizer(config);
        using var stream = recognizer.CreateStream();
        var samples = ReadPcm16MonoSamples(normalizedWavPath);

        stream.AcceptWaveform(SampleRate, samples);
        recognizer.Decode([stream]);

        return stream.Result.Text;
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
