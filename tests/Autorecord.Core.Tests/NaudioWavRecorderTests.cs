using Autorecord.Core.Audio;
using NAudio.Wave;

namespace Autorecord.Core.Tests;

public sealed class NaudioWavRecorderTests
{
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
