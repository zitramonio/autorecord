using Autorecord.Core.Audio;
using NAudio.Wave;

namespace Autorecord.Core.Transcription.Pipeline;

public sealed record NormalizedAudio(string NormalizedWavPath, bool CreatedTemporaryFile);

public sealed class AudioNormalizer
{
    private static readonly WaveFormat TargetFormat = new(16_000, 16, 1);
    private readonly string _tempRoot;

    public AudioNormalizer(string tempRoot)
    {
        _tempRoot = tempRoot;
    }

    public Task<NormalizedAudio> NormalizeAsync(
        string inputPath,
        bool keepIntermediateFiles,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var readerInputPath = inputPath;
            string? repairedWavPath = null;
            if (IsWav(inputPath))
            {
                try
                {
                    if (IsAlreadyNormalizedWav(inputPath))
                    {
                        return new NormalizedAudio(inputPath, CreatedTemporaryFile: false);
                    }
                }
                catch (FormatException)
                {
                    Directory.CreateDirectory(_tempRoot);
                    repairedWavPath = CreateRepairedOutputPath();
                    if (!WavFileRepair.TryCreateRepairedCopy(inputPath, repairedWavPath))
                    {
                        throw;
                    }

                    readerInputPath = repairedWavPath;
                }
            }

            Directory.CreateDirectory(_tempRoot);
            var outputPath = CreateNormalizedOutputPath();
            var stagingPath = $"{outputPath}.tmp";

            try
            {
                using var reader = CreateReader(readerInputPath);
                using var resampler = new MediaFoundationResampler(reader, TargetFormat)
                {
                    ResamplerQuality = 60
                };

                WaveFileWriter.CreateWaveFile(stagingPath, resampler);
                File.Move(stagingPath, outputPath);
                return new NormalizedAudio(outputPath, CreatedTemporaryFile: true);
            }
            catch
            {
                if (File.Exists(stagingPath))
                {
                    File.Delete(stagingPath);
                }

                throw;
            }
            finally
            {
                if (!keepIntermediateFiles &&
                    !string.IsNullOrWhiteSpace(repairedWavPath) &&
                    File.Exists(repairedWavPath))
                {
                    File.Delete(repairedWavPath);
                }
            }
        }, cancellationToken);
    }

    private static bool IsWav(string inputPath)
    {
        return inputPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAlreadyNormalizedWav(string inputPath)
    {
        if (!IsWav(inputPath))
        {
            return false;
        }

        using var reader = new WaveFileReader(inputPath);
        var format = NormalizeFormat(reader.WaveFormat);

        return format.Encoding == WaveFormatEncoding.Pcm &&
            format.SampleRate == TargetFormat.SampleRate &&
            format.Channels == TargetFormat.Channels &&
            format.BitsPerSample == TargetFormat.BitsPerSample;
    }

    private static WaveStream CreateReader(string inputPath)
    {
        if (IsWav(inputPath))
        {
            return new WaveFileReader(inputPath);
        }

        return new MediaFoundationReader(inputPath);
    }

    private string CreateNormalizedOutputPath()
    {
        return Path.Combine(_tempRoot, $"{Guid.NewGuid():N}.normalized.wav");
    }

    private string CreateRepairedOutputPath()
    {
        return Path.Combine(_tempRoot, $"{Guid.NewGuid():N}.repaired.wav");
    }

    private static WaveFormat NormalizeFormat(WaveFormat format)
    {
        return format is WaveFormatExtensible extensible
            ? extensible.ToStandardWaveFormat()
            : format;
    }
}
