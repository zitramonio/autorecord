using Autorecord.Core.Audio;
using NAudio.Wave;

namespace Autorecord.Core.Tests;

public sealed class NaudioWavRecorderTests
{
    [Fact]
    public async Task PrepareAsyncStartsOnlyInputCapture()
    {
        var factory = new FakeAudioCaptureSessionFactory();
        await using var recorder = new NaudioWavRecorder(factory);

        await recorder.PrepareAsync(CancellationToken.None);

        Assert.Equal(1, factory.InputCreateCount);
        Assert.Equal(0, factory.RenderCreateCount);
        Assert.Single(factory.Sessions, session => session.IsStarted);
        Assert.True(factory.Sessions.Single().IsInput);
    }

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
    public void CalculateDueFramesUsesElapsedTimeWithLatency()
    {
        var elapsed = TimeSpan.FromMinutes(5);

        var dueFrames = NaudioWavRecorder.CalculateDueFrames(
            elapsed,
            sampleRate: 48_000,
            latency: TimeSpan.FromMilliseconds(200));

        Assert.Equal(14_390_400, dueFrames);
    }

    [Fact]
    public void CalculateFinalFramesUsesExactStopElapsedTime()
    {
        var elapsed = TimeSpan.FromMinutes(5) + TimeSpan.FromMilliseconds(375);

        var finalFrames = NaudioWavRecorder.CalculateFinalFrames(elapsed, sampleRate: 48_000);

        Assert.Equal(14_418_000, finalFrames);
    }

    [Fact]
    public void CalculateSamplesToWriteDoesNotExceedTargetOrBuffer()
    {
        var samples = NaudioWavRecorder.CalculateSamplesToWrite(
            targetFrames: 48_000,
            framesWritten: 47_990,
            channels: 2,
            maxSamples: 960);

        Assert.Equal(20, samples);
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
    public void GetTechnicalTrackPathsUseMicAndSystemSuffixes()
    {
        Assert.Equal(
            @"C:\Recordings\07.05.2026 15.10.mic.recording.wav",
            NaudioWavRecorder.GetTemporaryTechnicalTrackPath(@"C:\Recordings\07.05.2026 15.10.mp3", "mic"));
        Assert.Equal(
            @"C:\Recordings\07.05.2026 15.10.system.recording.wav",
            NaudioWavRecorder.GetTemporaryTechnicalTrackPath(@"C:\Recordings\07.05.2026 15.10.mp3", "system"));
        Assert.Equal(
            @"C:\Recordings\07.05.2026 15.10.mic.wav",
            NaudioWavRecorder.GetFinalTechnicalTrackPath(@"C:\Recordings\07.05.2026 15.10.mp3", "mic"));
        Assert.Equal(
            @"C:\Recordings\07.05.2026 15.10.system.wav",
            NaudioWavRecorder.GetFinalTechnicalTrackPath(@"C:\Recordings\07.05.2026 15.10.mp3", "system"));
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
    public void FinalizeTemporaryWavToMp3MovesTechnicalTrackWavsNextToFinalMp3()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var wavPath = Path.Combine(dir, "meeting.recording.wav");
        var micTempPath = Path.Combine(dir, "meeting.mic.recording.wav");
        var systemTempPath = Path.Combine(dir, "meeting.system.recording.wav");
        var mp3Path = Path.Combine(dir, "meeting.mp3");
        var micPath = Path.Combine(dir, "meeting.mic.wav");
        var systemPath = Path.Combine(dir, "meeting.system.wav");
        try
        {
            CreateShortWav(wavPath);
            CreateShortWav(micTempPath);
            CreateShortWav(systemTempPath);

            var savedPath = NaudioWavRecorder.FinalizeTemporaryWavToMp3(wavPath, mp3Path);

            Assert.Equal(mp3Path, savedPath);
            Assert.True(File.Exists(mp3Path));
            Assert.True(File.Exists(micPath));
            Assert.True(File.Exists(systemPath));
            Assert.False(File.Exists(wavPath));
            Assert.False(File.Exists(micTempPath));
            Assert.False(File.Exists(systemTempPath));
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
    public void FinalizeTemporaryWavToMp3RepairsUnfinalizedTemporaryWav()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var wavPath = Path.Combine(dir, "meeting.recording.wav");
        var mp3Path = Path.Combine(dir, "meeting.mp3");
        try
        {
            using (var writer = new WaveFileWriter(wavPath, new WaveFormat(48_000, 16, 2)))
            {
                writer.Write(new byte[48_000 * 2 * 2 / 10], 0, 48_000 * 2 * 2 / 10);
            }

            ZeroWavChunkSizes(wavPath);

            var savedPath = NaudioWavRecorder.FinalizeTemporaryWavToMp3(wavPath, mp3Path);

            Assert.Equal(mp3Path, savedPath);
            Assert.True(File.Exists(mp3Path));
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

    [Fact]
    public void AudioFileSavedEventArgsCarriesTechnicalTrackPaths()
    {
        var args = new AudioFileSavedEventArgs(
            "requested.mp3",
            "saved.mp3",
            "combined.recording.wav",
            "saved.mic.wav",
            "saved.system.wav");

        Assert.Equal("saved.mic.wav", args.MicrophoneTrackPath);
        Assert.Equal("saved.system.wav", args.SystemTrackPath);
    }

    [Fact]
    public async Task StopAsyncSavesSeparateMicrophoneAndSystemTrackWavs()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var outputPath = Path.Combine(dir, "meeting.mp3");
        var factory = new FakeAudioCaptureSessionFactory();
        await using var recorder = new NaudioWavRecorder(factory);
        var saved = new TaskCompletionSource<AudioFileSavedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        recorder.FileSaved += (_, args) => saved.TrySetResult(args);

        try
        {
            await recorder.StartAsync(outputPath, CancellationToken.None);
            factory.InputSession?.RaiseFloatSamples(0.5f, frames: 48_000);
            factory.RenderSession?.RaiseFloatSamples(0.25f, frames: 48_000);
            await Task.Delay(300);

            await recorder.StopAsync(CancellationToken.None);
            var args = await saved.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.True(File.Exists(args.SavedOutputPath));
            Assert.NotNull(args.MicrophoneTrackPath);
            Assert.NotNull(args.SystemTrackPath);
            Assert.True(File.Exists(args.MicrophoneTrackPath));
            Assert.True(File.Exists(args.SystemTrackPath));
            Assert.True(ReadPeakFromPcm16Wav(args.MicrophoneTrackPath) > 0);
            Assert.True(ReadPeakFromPcm16Wav(args.SystemTrackPath) > 0);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    private sealed class FakeAudioCaptureSessionFactory : IAudioCaptureSessionFactory
    {
        public int InputCreateCount { get; private set; }
        public int RenderCreateCount { get; private set; }
        public List<FakeAudioCaptureSession> Sessions { get; } = [];
        public FakeAudioCaptureSession? InputSession { get; private set; }
        public FakeAudioCaptureSession? RenderSession { get; private set; }

        public IAudioCaptureSession CreateInput(WaveFormat waveFormat)
        {
            InputCreateCount++;
            var session = new FakeAudioCaptureSession(waveFormat, isInput: true);
            InputSession = session;
            Sessions.Add(session);
            return session;
        }

        public IReadOnlyList<IAudioCaptureSession> CreateRenderLoopbacks()
        {
            RenderCreateCount++;
            var session = new FakeAudioCaptureSession(WaveFormat.CreateIeeeFloatWaveFormat(44_100, 2), isInput: false);
            RenderSession = session;
            Sessions.Add(session);
            return [session];
        }
    }

    private sealed class FakeAudioCaptureSession(WaveFormat waveFormat, bool isInput) : IAudioCaptureSession
    {
        public event EventHandler<WaveInEventArgs>? DataAvailable;

        public WaveFormat WaveFormat { get; } = waveFormat;
        public bool IsInput { get; } = isInput;
        public bool IsStarted { get; private set; }
        public bool IsDisposed { get; private set; }

        public void StartRecording()
        {
            IsStarted = true;
            DataAvailable?.Invoke(this, new WaveInEventArgs([], 0));
        }

        public void StopRecording()
        {
            IsStarted = false;
        }

        public void Dispose()
        {
            IsDisposed = true;
        }

        public void RaiseFloatSamples(float value, int frames)
        {
            var bytes = new byte[frames * WaveFormat.Channels * sizeof(float)];
            for (var offset = 0; offset < bytes.Length; offset += sizeof(float))
            {
                BitConverter.GetBytes(value).CopyTo(bytes, offset);
            }

            DataAvailable?.Invoke(this, new WaveInEventArgs(bytes, bytes.Length));
        }
    }

    private static void ZeroWavChunkSizes(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        stream.Position = 4;
        stream.Write(new byte[4]);

        var marker = new byte[4];
        var sizeBytes = new byte[4];
        var dataMarker = "data"u8.ToArray();
        stream.Position = 12;
        while (stream.Position + 8 <= stream.Length)
        {
            var chunkStart = stream.Position;
            _ = stream.Read(marker);
            _ = stream.Read(sizeBytes);
            var chunkSize = BitConverter.ToUInt32(sizeBytes);
            if (marker.SequenceEqual(dataMarker))
            {
                stream.Position = chunkStart + 4;
                stream.Write(new byte[4]);
                return;
            }

            stream.Position = chunkStart + 8 + chunkSize + (chunkSize % 2);
        }

        throw new InvalidOperationException("Could not find data chunk.");
    }

    private static void CreateShortWav(string path)
    {
        using var writer = new WaveFileWriter(path, new WaveFormat(48_000, 16, 2));
        writer.Write(new byte[48_000 * 2 * 2 / 10], 0, 48_000 * 2 * 2 / 10);
    }

    private static float ReadPeakFromPcm16Wav(string path)
    {
        using var reader = new WaveFileReader(path);
        var buffer = new byte[reader.WaveFormat.AverageBytesPerSecond];
        var bytesRead = reader.Read(buffer, 0, buffer.Length);
        return NaudioWavRecorder.CalculatePeak(buffer, bytesRead, reader.WaveFormat);
    }
}
