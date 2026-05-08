using Autorecord.Core.Transcription.Diarization;
using Autorecord.Core.Transcription.Results;

namespace Autorecord.Core.Tests;

public sealed class DiarizationEngineTests
{
    private const string EmbeddingModelFileName = "3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx";

    [Fact]
    public void NormalizeTurnsMergesSameSpeakerAndDropsShortTurns()
    {
        var turns = new[]
        {
            new DiarizationTurn(0, 1, "S0"),
            new DiarizationTurn(1.6, 2, "S0"),
            new DiarizationTurn(2.8, 3.0, "S0"),
            new DiarizationTurn(3.2, 4, "S1"),
        };

        var normalized = DiarizationEngine.NormalizeTurns(turns);

        Assert.Collection(
            normalized,
            turn => Assert.Equal(new DiarizationTurn(0, 2, "S0"), turn),
            turn => Assert.Equal(new DiarizationTurn(3.2, 4, "S1"), turn));
    }

    [Fact]
    public void NormalizeTurnsDoesNotMergeDifferentSpeakers()
    {
        var turns = new[]
        {
            new DiarizationTurn(0, 1, "S0"),
            new DiarizationTurn(1.3, 2, "S1"),
        };

        var normalized = DiarizationEngine.NormalizeTurns(turns);

        Assert.Collection(
            normalized,
            turn => Assert.Equal(new DiarizationTurn(0, 1, "S0"), turn),
            turn => Assert.Equal(new DiarizationTurn(1.3, 2, "S1"), turn));
    }

    [Fact]
    public void NormalizeTurnsSortsTurnsByStart()
    {
        var turns = new[]
        {
            new DiarizationTurn(3, 4, "S1"),
            new DiarizationTurn(0, 1, "S0"),
            new DiarizationTurn(1.8, 2.8, "S0"),
        };

        var normalized = DiarizationEngine.NormalizeTurns(turns);

        Assert.Collection(
            normalized,
            turn => Assert.Equal(new DiarizationTurn(0, 1, "S0"), turn),
            turn => Assert.Equal(new DiarizationTurn(1.8, 2.8, "S0"), turn),
            turn => Assert.Equal(new DiarizationTurn(3, 4, "S1"), turn));
    }

    [Fact]
    public void NormalizeTurnsThrowsWhenTurnsIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => DiarizationEngine.NormalizeTurns(null!));
    }

    [Fact]
    public void NormalizeTurnsThrowsWhenTurnElementIsNull()
    {
        var turns = new DiarizationTurn[] { null! };

        Assert.Throws<ArgumentException>(() => DiarizationEngine.NormalizeTurns(turns));
    }

    [Theory]
    [InlineData(-0.1, 1)]
    [InlineData(0, -0.1)]
    public void NormalizeTurnsThrowsWhenTimestampIsNegative(double start, double end)
    {
        var turns = new[] { new DiarizationTurn(start, end, "S0") };

        Assert.Throws<ArgumentException>(() => DiarizationEngine.NormalizeTurns(turns));
    }

    [Theory]
    [InlineData(double.NaN, 1)]
    [InlineData(0, double.NaN)]
    [InlineData(double.PositiveInfinity, 1)]
    [InlineData(0, double.NegativeInfinity)]
    public void NormalizeTurnsThrowsWhenTimestampIsNaNOrInfinity(double start, double end)
    {
        var turns = new[] { new DiarizationTurn(start, end, "S0") };

        Assert.Throws<ArgumentException>(() => DiarizationEngine.NormalizeTurns(turns));
    }

    [Fact]
    public void NormalizeTurnsThrowsWhenEndIsBeforeStart()
    {
        var turns = new[] { new DiarizationTurn(2, 1, "S0") };

        Assert.Throws<ArgumentException>(() => DiarizationEngine.NormalizeTurns(turns));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void NormalizeTurnsThrowsWhenSpeakerIdIsBlank(string? speakerId)
    {
        var turns = new[] { new DiarizationTurn(0, 1, speakerId!) };

        Assert.Throws<ArgumentException>(() => DiarizationEngine.NormalizeTurns(turns));
    }

    [Fact]
    public void FormatSpeakerIdUsesTwoDigitSpeakerPrefix()
    {
        Assert.Equal("SPEAKER_00", DiarizationEngine.FormatSpeakerId(0));
        Assert.Equal("SPEAKER_12", DiarizationEngine.FormatSpeakerId(12));
    }

    [Fact]
    public void NormalizeTurnsAndReportCompletionDoesNotReport100WhenCleanupFails()
    {
        var progress = new RecordingProgress();
        var turns = new[] { new DiarizationTurn(2, 1, "S0") };

        Assert.Throws<ArgumentException>(() => DiarizationEngine.NormalizeTurnsAndReportCompletion(turns, progress));
        Assert.DoesNotContain(100, progress.Values);
    }

    [Fact]
    public async Task DiarizeAsyncThrowsWhenSegmentationModelIsMissing()
    {
        var root = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, EmbeddingModelFileName), "test");
            var engine = new DiarizationEngine();

            var exception = await Assert.ThrowsAsync<FileNotFoundException>(
                () => engine.DiarizeAsync("normalized.wav", root, null, null, new Progress<int>(), CancellationToken.None));

            Assert.Contains("model.onnx", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("model.int8.onnx", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task DiarizeAsyncThrowsWhenEmbeddingModelIsMissing()
    {
        var root = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "model.int8.onnx"), "test");
            var engine = new DiarizationEngine();

            var exception = await Assert.ThrowsAsync<FileNotFoundException>(
                () => engine.DiarizeAsync("normalized.wav", root, null, null, new Progress<int>(), CancellationToken.None));

            Assert.Contains(EmbeddingModelFileName, exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public async Task DiarizeAsyncRejectsInvalidSpeakerCount(int numSpeakers)
    {
        var engine = new DiarizationEngine();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => engine.DiarizeAsync("normalized.wav", "models", numSpeakers, null, new Progress<int>(), CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public async Task DiarizeAsyncRejectsInvalidClusterThreshold(double clusterThreshold)
    {
        var engine = new DiarizationEngine();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => engine.DiarizeAsync("normalized.wav", "models", null, clusterThreshold, new Progress<int>(), CancellationToken.None));
    }

    [Fact]
    public async Task DiarizeAsyncHonorsCancellationBeforeValidation()
    {
        var engine = new DiarizationEngine();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => engine.DiarizeAsync("normalized.wav", "missing-models", null, null, new Progress<int>(), cancellation.Token));
    }

    [Fact]
    public async Task DiarizeAsyncProcessesLongWavInPaddedChunks()
    {
        var root = CreateTempDirectory();
        try
        {
            CreateRequiredModelFiles(root);
            var wavPath = Path.Combine(root, "long.wav");
            CreatePcm16MonoWav(wavPath, durationSeconds: 45);

            var decoder = new RecordingDiarizationDecoder(
                [new DiarizationTurn(0, 20, "S0")],
                [new DiarizationTurn(5, 25, "S1")],
                [new DiarizationTurn(5, 10, "S2")]);
            var engine = new DiarizationEngine(
                new RecordingDiarizationDecoderFactory(decoder),
                new DiarizationChunkOptions(ChunkSeconds: 20, PaddingSeconds: 5));
            var progress = new RecordingProgress();

            var turns = await engine.DiarizeAsync(wavPath, root, null, null, progress, CancellationToken.None);

            Assert.Equal(3, decoder.SampleCounts.Count);
            Assert.All(decoder.SampleCounts, sampleCount => Assert.True(sampleCount < 45 * 16_000));
            Assert.Equal([25d, 30d, 10d], decoder.SampleCounts.Select(count => count / 16_000d));
            Assert.Equal([0, 44, 88, 100], progress.Values);
            Assert.Equal(
                [
                    new DiarizationTurn(0, 20, "SPEAKER_00"),
                    new DiarizationTurn(20, 40, "SPEAKER_01"),
                    new DiarizationTurn(40, 45, "SPEAKER_02"),
                ],
                turns);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task DiarizeAsyncHonorsCancellationBetweenChunks()
    {
        var root = CreateTempDirectory();
        try
        {
            CreateRequiredModelFiles(root);
            var wavPath = Path.Combine(root, "long.wav");
            CreatePcm16MonoWav(wavPath, durationSeconds: 45);

            using var cancellation = new CancellationTokenSource();
            var decoder = new RecordingDiarizationDecoder(
                [new DiarizationTurn(0, 20, "S0")],
                [new DiarizationTurn(0, 20, "S1")],
                [new DiarizationTurn(0, 5, "S2")]);
            var engine = new DiarizationEngine(
                new RecordingDiarizationDecoderFactory(decoder),
                new DiarizationChunkOptions(ChunkSeconds: 20, PaddingSeconds: 5));
            var progress = new CallbackProgress(value =>
            {
                if (value > 0)
                {
                    cancellation.Cancel();
                }
            });

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => engine.DiarizeAsync(wavPath, root, null, null, progress, cancellation.Token));
            Assert.Single(decoder.SampleCounts);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task DiarizeAsyncMapsSpeakerIdsAcrossChunksUsingPaddingOverlap()
    {
        var root = CreateTempDirectory();
        try
        {
            CreateRequiredModelFiles(root);
            var wavPath = Path.Combine(root, "long.wav");
            CreatePcm16MonoWav(wavPath, durationSeconds: 25);

            var decoder = new RecordingDiarizationDecoder(
                [new DiarizationTurn(15, 25, "left-local")],
                [new DiarizationTurn(0, 10, "right-local")]);
            var engine = new DiarizationEngine(
                new RecordingDiarizationDecoderFactory(decoder),
                new DiarizationChunkOptions(ChunkSeconds: 20, PaddingSeconds: 5));

            var turns = await engine.DiarizeAsync(wavPath, root, null, null, new RecordingProgress(), CancellationToken.None);

            Assert.Equal(
                [new DiarizationTurn(15, 25, "SPEAKER_00")],
                turns);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task DiarizeAsyncDoesNotMapSpeakerIdsAcrossArtificialMergeGaps()
    {
        var root = CreateTempDirectory();
        try
        {
            CreateRequiredModelFiles(root);
            var wavPath = Path.Combine(root, "long.wav");
            CreatePcm16MonoWav(wavPath, durationSeconds: 25);

            var decoder = new RecordingDiarizationDecoder(
                [
                    new DiarizationTurn(15, 17, "left-local"),
                    new DiarizationTurn(17.6, 20, "left-local"),
                ],
                [
                    new DiarizationTurn(2.15, 2.45, "right-local"),
                    new DiarizationTurn(5.1, 6, "right-local"),
                ]);
            var engine = new DiarizationEngine(
                new RecordingDiarizationDecoderFactory(decoder),
                new DiarizationChunkOptions(ChunkSeconds: 20, PaddingSeconds: 5));

            var turns = await engine.DiarizeAsync(wavPath, root, null, null, new RecordingProgress(), CancellationToken.None);

            Assert.Contains(new DiarizationTurn(20.1, 21, "SPEAKER_01"), turns);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task DiarizeAsyncReturnsCompletedResultWhenCancellationIsRequestedAfterFinalProgress()
    {
        var root = CreateTempDirectory();
        try
        {
            CreateRequiredModelFiles(root);
            var wavPath = Path.Combine(root, "short.wav");
            CreatePcm16MonoWav(wavPath, durationSeconds: 5);

            using var cancellation = new CancellationTokenSource();
            var decoder = new RecordingDiarizationDecoder([new DiarizationTurn(0, 5, "S0")]);
            var engine = new DiarizationEngine(
                new RecordingDiarizationDecoderFactory(decoder),
                new DiarizationChunkOptions(ChunkSeconds: 20, PaddingSeconds: 5));
            var progress = new CallbackProgress(value =>
            {
                if (value == 100)
                {
                    cancellation.Cancel();
                }
            });

            var turns = await engine.DiarizeAsync(wavPath, root, null, null, progress, cancellation.Token);

            Assert.Equal(new DiarizationTurn(0, 5, "SPEAKER_00"), Assert.Single(turns));
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
        File.WriteAllText(Path.Combine(root, "model.int8.onnx"), "test");
        File.WriteAllText(Path.Combine(root, EmbeddingModelFileName), "test");
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

    private sealed class RecordingProgress : IProgress<int>
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

    private sealed class RecordingDiarizationDecoderFactory(RecordingDiarizationDecoder decoder)
        : IDiarizationDecoderFactory
    {
        public IDiarizationDecoder Create(
            string segmentationModelPath,
            string embeddingModelPath,
            int? numSpeakers,
            double? clusterThreshold)
        {
            return decoder;
        }
    }

    private sealed class RecordingDiarizationDecoder(params IReadOnlyList<DiarizationTurn>[] turns)
        : IDiarizationDecoder
    {
        private int nextTurnIndex;

        public List<int> SampleCounts { get; } = [];

        public IReadOnlyList<DiarizationTurn> Decode(float[] samples)
        {
            SampleCounts.Add(samples.Length);
            return turns[nextTurnIndex++];
        }

        public void Dispose()
        {
        }
    }
}
