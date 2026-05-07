using Autorecord.Core.Audio;
using NAudio.Wave;

namespace Autorecord.Core.Tests;

public sealed class NaudioWavRecorderTests
{
    [Fact]
    public void ShouldStopWriterWaitsForBufferedAudioAfterStopRequested()
    {
        var input = NaudioWavRecorder.CreateBufferedProviderForSource(WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2));
        var output = NaudioWavRecorder.CreateBufferedProviderForSource(WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2));
        input.AddSamples(new byte[48_000 * 2 * 4], 0, 48_000 * 2 * 4);

        var shouldStop = NaudioWavRecorder.ShouldStopWriter(true, input, output);

        Assert.False(shouldStop);
    }

    [Fact]
    public void ShouldStopWriterStopsAfterAllBuffersAreDrained()
    {
        var input = NaudioWavRecorder.CreateBufferedProviderForSource(WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2));
        var output = NaudioWavRecorder.CreateBufferedProviderForSource(WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2));

        var shouldStop = NaudioWavRecorder.ShouldStopWriter(true, input, output);

        Assert.True(shouldStop);
    }

    [Fact]
    public void SourceBuffersKeepMoreThanShortManualTestRecording()
    {
        var provider = NaudioWavRecorder.CreateBufferedProviderForSource(WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2));

        Assert.True(provider.BufferDuration >= TimeSpan.FromSeconds(30));
        Assert.False(provider.DiscardOnBufferOverflow);
    }

    [Fact]
    public void ConvertToMixedFormatBypassesResamplerWhenSourceAlreadyMatches()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);
        var buffer = new byte[16];
        BitConverter.GetBytes(0.25f).CopyTo(buffer, 0);
        BitConverter.GetBytes(-0.25f).CopyTo(buffer, 4);
        BitConverter.GetBytes(0.5f).CopyTo(buffer, 8);
        BitConverter.GetBytes(-0.5f).CopyTo(buffer, 12);

        var converted = NaudioWavRecorder.ConvertToMixedFormat(buffer, buffer.Length, format);

        Assert.Equal(buffer, converted);
    }

    [Fact]
    public void GetTemporaryWavPathAddsTemporarySuffixBeforeMp3Extension()
    {
        var path = NaudioWavRecorder.GetTemporaryWavPath(@"C:\Recordings\07.05.2026 15.10.mp3");

        Assert.Equal(@"C:\Recordings\07.05.2026 15.10.recording.wav", path);
    }

    [Fact]
    public void EncodeTemporaryWavToMp3CreatesMp3File()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var wavPath = Path.Combine(dir, "input.recording.wav");
        var mp3Path = Path.Combine(dir, "output.mp3");
        try
        {
            using (var writer = new WaveFileWriter(wavPath, new WaveFormat(48_000, 16, 2)))
            {
                writer.Write(new byte[48_000 * 2 * 2 / 10], 0, 48_000 * 2 * 2 / 10);
            }

            NaudioWavRecorder.EncodeTemporaryWavToMp3(wavPath, mp3Path);

            Assert.True(File.Exists(mp3Path));
            Assert.True(new FileInfo(mp3Path).Length > 0);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void FinalizeTemporaryWavToMp3MovesTempMp3ToFinalAndDeletesWav()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var wavPath = Path.Combine(dir, "meeting.recording.wav");
        var mp3Path = Path.Combine(dir, "meeting.mp3");
        var tmpMp3Path = $"{mp3Path}.tmp";
        var encodingMp3Path = $"{mp3Path}.encoding.mp3";
        try
        {
            using (var writer = new WaveFileWriter(wavPath, new WaveFormat(48_000, 16, 2)))
            {
                writer.Write(new byte[48_000 * 2 * 2 / 10], 0, 48_000 * 2 * 2 / 10);
            }

            var savedPath = NaudioWavRecorder.FinalizeTemporaryWavToMp3(wavPath, mp3Path);

            Assert.Equal(mp3Path, savedPath);
            Assert.True(File.Exists(mp3Path));
            Assert.False(File.Exists(tmpMp3Path));
            Assert.False(File.Exists(encodingMp3Path));
            Assert.False(File.Exists(wavPath));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void FinalizeTemporaryWavToMp3KeepsWavWhenEncodingFails()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var wavPath = Path.Combine(dir, "broken.recording.wav");
        var mp3Path = Path.Combine(dir, "broken.mp3");
        try
        {
            File.WriteAllText(wavPath, "not a wav");

            Assert.Throws<FormatException>(() => NaudioWavRecorder.FinalizeTemporaryWavToMp3(wavPath, mp3Path));

            Assert.True(File.Exists(wavPath));
            Assert.False(File.Exists(mp3Path));
            Assert.False(File.Exists($"{mp3Path}.tmp"));
            Assert.False(File.Exists($"{mp3Path}.encoding.mp3"));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void CalculatePeakHandlesWaveFormatExtensibleFloat()
    {
        var format = new WaveFormatExtensible(48_000, 32, 2, 3);
        var buffer = new byte[8];
        BitConverter.GetBytes(0.25f).CopyTo(buffer, 0);
        BitConverter.GetBytes(-0.75f).CopyTo(buffer, 4);

        var peak = NaudioWavRecorder.CalculatePeak(buffer, buffer.Length, format);

        Assert.Equal(0.75f, peak, 3);
    }

    [Fact]
    public void CalculatePeakHandlesTwentyFourBitPcm()
    {
        var format = new WaveFormat(48_000, 24, 1);
        var buffer = new byte[] { 0x00, 0x00, 0x40 };

        var peak = NaudioWavRecorder.CalculatePeak(buffer, buffer.Length, format);

        Assert.Equal(0.5f, peak, 3);
    }

    [Fact]
    public void CalculatePeakHandlesThirtyTwoBitPcm()
    {
        var format = new WaveFormat(48_000, 32, 1);
        var buffer = BitConverter.GetBytes(int.MinValue);

        var peak = NaudioWavRecorder.CalculatePeak(buffer, buffer.Length, format);

        Assert.Equal(1f, peak, 3);
    }

    [Fact]
    public void CalculatePeakFallsBackToNonZeroForUnsupportedSignal()
    {
        var format = WaveFormat.CreateMuLawFormat(8_000, 1);
        var buffer = new byte[] { 0, 0, 12, 0 };

        var peak = NaudioWavRecorder.CalculatePeak(buffer, buffer.Length, format);

        Assert.True(peak > 0);
    }

}
