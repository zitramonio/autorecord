using Autorecord.Core.Transcription.Engines;

namespace Autorecord.Core.Tests;

public sealed class SherpaOnnxTranscriptionEngineTests
{
    [Fact]
    public async Task TranscribeAsyncThrowsWhenRequiredModelFilesAreMissing()
    {
        var root = CreateTempDirectory();
        try
        {
            var engine = new SherpaOnnxTranscriptionEngine();

            var exception = await Assert.ThrowsAsync<FileNotFoundException>(
                () => engine.TranscribeAsync("normalized.wav", root, new Progress<int>(), CancellationToken.None));

            Assert.Contains("tokens.txt", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task TranscribeAsyncThrowsWhenModelFileIsMissing()
    {
        var root = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "tokens.txt"), "test");
            var engine = new SherpaOnnxTranscriptionEngine();

            var exception = await Assert.ThrowsAsync<FileNotFoundException>(
                () => engine.TranscribeAsync("normalized.wav", root, new Progress<int>(), CancellationToken.None));

            Assert.Contains("model.int8.onnx", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task TranscribeAsyncThrowsWhenParakeetJoinerFileIsMissing()
    {
        var root = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "tokens.txt"), "test");
            await File.WriteAllTextAsync(Path.Combine(root, "encoder.int8.onnx"), "test");
            await File.WriteAllTextAsync(Path.Combine(root, "decoder.int8.onnx"), "test");
            var engine = new SherpaOnnxTranscriptionEngine();

            var exception = await Assert.ThrowsAsync<FileNotFoundException>(
                () => engine.TranscribeAsync("normalized.wav", root, new Progress<int>(), CancellationToken.None));

            Assert.Contains("joiner.int8.onnx", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task TranscribeAsyncHonorsCancellationBeforeValidation()
    {
        var root = CreateTempDirectory();
        try
        {
            var engine = new SherpaOnnxTranscriptionEngine();
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => engine.TranscribeAsync("normalized.wav", root, new Progress<int>(), cancellation.Token));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task TranscribeAsyncProcessesLongWavInPaddedChunks()
    {
        var root = CreateTempDirectory();
        try
        {
            CreateRequiredModelFiles(root);
            var wavPath = Path.Combine(root, "long.wav");
            CreatePcm16MonoWav(wavPath, durationSeconds: 45);

            var decoder = new RecordingChunkDecoder("first", "second", "third");
            var engine = new SherpaOnnxTranscriptionEngine(new RecordingChunkDecoderFactory(decoder));
            var progress = new CollectingProgress();

            var result = await engine.TranscribeAsync(wavPath, root, progress, CancellationToken.None);

            Assert.Equal(3, decoder.SampleCounts.Count);
            Assert.All(decoder.SampleCounts, sampleCount => Assert.True(sampleCount < 45 * 16_000));
            Assert.Equal([20.25, 20.5, 5.25], decoder.SampleCounts.Select(count => count / 16_000d));
            Assert.Equal([0, 44, 88, 100], progress.Values);
            Assert.Equal(
                [(0d, 20d, "first"), (20d, 40d, "second"), (40d, 45d, "third")],
                result.Segments.Select(segment => (segment.Start, segment.End, segment.Text)));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task TranscribeAsyncProcessesRequestedIntervalsOnly()
    {
        var root = CreateTempDirectory();
        try
        {
            CreateRequiredModelFiles(root);
            var wavPath = Path.Combine(root, "long.wav");
            CreatePcm16MonoWav(wavPath, durationSeconds: 45);

            var decoder = new RecordingChunkDecoder("first", "second");
            var engine = new SherpaOnnxTranscriptionEngine(new RecordingChunkDecoderFactory(decoder));
            var progress = new CollectingProgress();

            var result = await engine.TranscribeAsync(
                wavPath,
                root,
                [new TranscriptionEngineInterval(5, 7), new TranscriptionEngineInterval(30, 45)],
                progress,
                CancellationToken.None);

            Assert.Equal([2.5, 15.25], decoder.SampleCounts.Select(count => count / 16_000d));
            Assert.Equal([0, 11, 100], progress.Values);
            Assert.Equal(
                [(5d, 7d, "first"), (30d, 45d, "second")],
                result.Segments.Select(segment => (segment.Start, segment.End, segment.Text)));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task TranscribeAsyncUsesParakeetTransducerFilesWhenPresent()
    {
        var root = CreateTempDirectory();
        try
        {
            CreateParakeetModelFiles(root);
            var wavPath = Path.Combine(root, "short.wav");
            CreatePcm16MonoWav(wavPath, durationSeconds: 5);

            var decoder = new RecordingChunkDecoder("done");
            var factory = new RecordingChunkDecoderFactory(decoder);
            var engine = new SherpaOnnxTranscriptionEngine(factory);

            var result = await engine.TranscribeAsync(wavPath, root, new Progress<int>(), CancellationToken.None);

            Assert.Equal(SherpaOnnxModelKind.Transducer, factory.CreatedModelFiles?.Kind);
            Assert.EndsWith("encoder.int8.onnx", factory.CreatedModelFiles?.EncoderPath, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith("decoder.int8.onnx", factory.CreatedModelFiles?.DecoderPath, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith("joiner.int8.onnx", factory.CreatedModelFiles?.JoinerPath, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("done", Assert.Single(result.Segments).Text);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task TranscribeAsyncHonorsCancellationBetweenChunks()
    {
        var root = CreateTempDirectory();
        try
        {
            CreateRequiredModelFiles(root);
            var wavPath = Path.Combine(root, "long.wav");
            CreatePcm16MonoWav(wavPath, durationSeconds: 45);

            using var cancellation = new CancellationTokenSource();
            var decoder = new RecordingChunkDecoder("first", "second", "third");
            var engine = new SherpaOnnxTranscriptionEngine(new RecordingChunkDecoderFactory(decoder));
            var progress = new CallbackProgress(value =>
            {
                if (value > 0)
                {
                    cancellation.Cancel();
                }
            });

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => engine.TranscribeAsync(wavPath, root, progress, cancellation.Token));
            Assert.Single(decoder.SampleCounts);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task TranscribeAsyncReturnsCompletedResultWhenCancellationIsRequestedAfterFinalProgress()
    {
        var root = CreateTempDirectory();
        try
        {
            CreateRequiredModelFiles(root);
            var wavPath = Path.Combine(root, "short.wav");
            CreatePcm16MonoWav(wavPath, durationSeconds: 5);

            using var cancellation = new CancellationTokenSource();
            var decoder = new RecordingChunkDecoder("done");
            var engine = new SherpaOnnxTranscriptionEngine(new RecordingChunkDecoderFactory(decoder));
            var progress = new CallbackProgress(value =>
            {
                if (value == 100)
                {
                    cancellation.Cancel();
                }
            });

            var result = await engine.TranscribeAsync(wavPath, root, progress, cancellation.Token);

            Assert.Equal("done", Assert.Single(result.Segments).Text);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CreateRequiredModelFiles(string root)
    {
        File.WriteAllText(Path.Combine(root, "tokens.txt"), "test");
        File.WriteAllText(Path.Combine(root, "model.int8.onnx"), "test");
    }

    private static void CreateParakeetModelFiles(string root)
    {
        File.WriteAllText(Path.Combine(root, "tokens.txt"), "test");
        File.WriteAllText(Path.Combine(root, "encoder.int8.onnx"), "test");
        File.WriteAllText(Path.Combine(root, "decoder.int8.onnx"), "test");
        File.WriteAllText(Path.Combine(root, "joiner.int8.onnx"), "test");
    }

    private static void CreatePcm16MonoWav(string path, int durationSeconds)
    {
        const int sampleRate = 16_000;
        const short channelCount = 1;
        const short bitsPerSample = 16;
        const short blockAlign = channelCount * bitsPerSample / 8;
        const int byteRate = sampleRate * blockAlign;

        var sampleCount = checked(durationSeconds * sampleRate);
        var dataLength = checked(sampleCount * blockAlign);

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channelCount);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataLength);

        for (var i = 0; i < sampleCount; i++)
        {
            writer.Write((short)0);
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class RecordingChunkDecoderFactory(RecordingChunkDecoder decoder) : ISherpaOnnxChunkDecoderFactory
    {
        public SherpaOnnxModelFiles? CreatedModelFiles { get; private set; }

        public ISherpaOnnxChunkDecoder Create(SherpaOnnxModelFiles modelFiles)
        {
            CreatedModelFiles = modelFiles;
            return decoder;
        }
    }

    private sealed class RecordingChunkDecoder(params string[] texts) : ISherpaOnnxChunkDecoder
    {
        private int nextTextIndex;

        public List<int> SampleCounts { get; } = [];

        public string Decode(float[] samples)
        {
            SampleCounts.Add(samples.Length);
            return texts[nextTextIndex++];
        }

        public void Dispose()
        {
        }
    }

    private sealed class CollectingProgress : IProgress<int>
    {
        public List<int> Values { get; } = [];

        public void Report(int value)
        {
            Values.Add(value);
        }
    }

    private sealed class CallbackProgress(Action<int> callback) : IProgress<int>
    {
        public void Report(int value)
        {
            callback(value);
        }
    }
}
