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

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
