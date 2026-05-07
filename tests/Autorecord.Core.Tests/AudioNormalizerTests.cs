using Autorecord.Core.Audio;
using Autorecord.Core.Transcription.Pipeline;
using NAudio.Wave;

namespace Autorecord.Core.Tests;

public sealed class AudioNormalizerTests
{
    [Fact]
    public async Task NormalizeAsyncReturnsSamePathWhenWavAlreadyMatches()
    {
        var root = CreateTempRoot();
        try
        {
            var inputPath = Path.Combine(root, "input.wav");
            CreateSilentWav(inputPath, new WaveFormat(16_000, 16, 1));
            var tempRoot = Path.Combine(root, "normalized");
            var normalizer = new AudioNormalizer(tempRoot);

            var normalized = await normalizer.NormalizeAsync(inputPath, keepIntermediateFiles: false, CancellationToken.None);

            Assert.Equal(inputPath, normalized.NormalizedWavPath);
            Assert.False(normalized.CreatedTemporaryFile);
            Assert.False(Directory.Exists(tempRoot));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task NormalizeAsyncCreatesTempWavWhenWavFormatDiffers()
    {
        var root = CreateTempRoot();
        try
        {
            var inputPath = Path.Combine(root, "input.wav");
            CreateSilentWav(inputPath, new WaveFormat(48_000, 16, 2));
            var tempRoot = Path.Combine(root, "normalized");
            var normalizer = new AudioNormalizer(tempRoot);

            var normalized = await normalizer.NormalizeAsync(inputPath, keepIntermediateFiles: false, CancellationToken.None);

            Assert.NotEqual(inputPath, normalized.NormalizedWavPath);
            Assert.True(normalized.CreatedTemporaryFile);
            Assert.True(File.Exists(normalized.NormalizedWavPath));
            Assert.EndsWith(".normalized.wav", normalized.NormalizedWavPath, StringComparison.OrdinalIgnoreCase);
            AssertNormalizedWavFormat(normalized.NormalizedWavPath);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task NormalizeAsyncCreatesTempWavFromAppMp3()
    {
        var root = CreateTempRoot();
        try
        {
            var wavPath = Path.Combine(root, "input.recording.wav");
            var mp3Path = Path.Combine(root, "input.mp3");
            CreateSilentWav(wavPath, new WaveFormat(48_000, 16, 2));
            NaudioWavRecorder.EncodeTemporaryWavToMp3(wavPath, mp3Path);
            var tempRoot = Path.Combine(root, "normalized");
            var normalizer = new AudioNormalizer(tempRoot);

            var normalized = await normalizer.NormalizeAsync(mp3Path, keepIntermediateFiles: false, CancellationToken.None);

            Assert.NotEqual(mp3Path, normalized.NormalizedWavPath);
            Assert.True(normalized.CreatedTemporaryFile);
            Assert.True(File.Exists(normalized.NormalizedWavPath));
            AssertNormalizedWavFormat(normalized.NormalizedWavPath);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task NormalizeAsyncHonorsCancellationBeforeWork()
    {
        var root = CreateTempRoot();
        try
        {
            var inputPath = Path.Combine(root, "input.wav");
            CreateSilentWav(inputPath, new WaveFormat(48_000, 16, 2));
            var tempRoot = Path.Combine(root, "normalized");
            var normalizer = new AudioNormalizer(tempRoot);
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => normalizer.NormalizeAsync(inputPath, keepIntermediateFiles: false, cancellation.Token));

            Assert.False(Directory.Exists(tempRoot));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task NormalizeAsyncUsesUniqueOutputPath()
    {
        var root = CreateTempRoot();
        try
        {
            var inputPath = Path.Combine(root, "input.wav");
            CreateSilentWav(inputPath, new WaveFormat(48_000, 16, 2));
            var tempRoot = Path.Combine(root, "normalized");
            var normalizer = new AudioNormalizer(tempRoot);

            var first = await normalizer.NormalizeAsync(inputPath, keepIntermediateFiles: false, CancellationToken.None);
            var second = await normalizer.NormalizeAsync(inputPath, keepIntermediateFiles: false, CancellationToken.None);

            Assert.NotEqual(first.NormalizedWavPath, second.NormalizedWavPath);
            Assert.True(File.Exists(first.NormalizedWavPath));
            Assert.True(File.Exists(second.NormalizedWavPath));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static void CreateSilentWav(string path, WaveFormat waveFormat)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        using var writer = new WaveFileWriter(path, waveFormat);
        writer.Write(new byte[waveFormat.AverageBytesPerSecond / 10], 0, waveFormat.AverageBytesPerSecond / 10);
    }

    private static void AssertNormalizedWavFormat(string path)
    {
        using var reader = new WaveFileReader(path);

        Assert.Equal(WaveFormatEncoding.Pcm, reader.WaveFormat.Encoding);
        Assert.Equal(16_000, reader.WaveFormat.SampleRate);
        Assert.Equal(1, reader.WaveFormat.Channels);
        Assert.Equal(16, reader.WaveFormat.BitsPerSample);
    }

    private static string CreateTempRoot()
    {
        return Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
