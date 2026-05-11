using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Diagnostics;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Autorecord.Core.Tests")]

namespace Autorecord.Core.Audio;

public sealed class NaudioWavRecorder : IAudioRecorder
{
    private static readonly WaveFormat MixedWaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);
    private static readonly WaveFormat TemporaryWaveFormat = new(48_000, 16, 2);
    private static readonly TimeSpan WriterLatency = TimeSpan.FromMilliseconds(200);
    private const int Mp3Bitrate = 128_000;

    private readonly List<CaptureSource> _captureSources = [];
    private readonly List<BufferedWaveProvider> _sourceBuffers = [];
    private MixingSampleProvider? _mixer;
    private WaveFileWriter? _writer;
    private CancellationTokenSource? _writerCancellation;
    private Task? _writerTask;
    private Stopwatch? _recordingClock;
    private string? _outputPath;
    private string? _temporaryWavPath;
    private readonly object _gate = new();
    private readonly object _lifecycleGate = new();
    private float _lastInputPeak;
    private float _lastOutputPeak;
    private long _framesWritten;
    private long? _finalFramesToWrite;
    private bool _captureActive;
    private bool _recordingActive;
    private bool _restartCaptureAfterStop;
    private bool _writerStopRequested;
    private bool _captureStoppedForWriter;

    public event EventHandler<AudioLevel>? LevelChanged;
    public event EventHandler<AudioFileSavedEventArgs>? FileSaved;
    public event EventHandler<AudioFileSaveFailedEventArgs>? FileSaveFailed;

    public Task PrepareAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lifecycleGate)
        {
            EnsureCaptureStarted();
        }

        return Task.CompletedTask;
    }

    public Task StartAsync(string outputPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lifecycleGate)
        {
            if (_recordingActive)
            {
                throw new InvalidOperationException("Recorder is already started.");
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
                _outputPath = outputPath;
                _temporaryWavPath = GetTemporaryWavPath(outputPath);
                _restartCaptureAfterStop = _captureActive;
                EnsureCaptureStarted();
                _sourceBuffers.Clear();
                foreach (var source in _captureSources)
                {
                    source.Buffer = CreateBufferedProviderForSource(MixedWaveFormat);
                    _sourceBuffers.Add(source.Buffer);
                }

                _mixer = new MixingSampleProvider(
                    _sourceBuffers.Select(buffer => new WaveToSampleProvider(buffer)))
                {
                    ReadFully = true
                };
                _writer = new WaveFileWriter(_temporaryWavPath, TemporaryWaveFormat);
                _writerCancellation = new CancellationTokenSource();
                _recordingClock = Stopwatch.StartNew();
                _framesWritten = 0;
                _finalFramesToWrite = null;
                _writerStopRequested = false;
                _captureStoppedForWriter = false;
                _recordingActive = true;
                _writerTask = Task.Run(() => RunWriterLoopAsync(_writerCancellation.Token), CancellationToken.None);
            }
            catch
            {
                StopCore(scheduleConversion: false);
                throw;
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lifecycleGate)
        {
            StopCore(scheduleConversion: true);
        }

        return Task.CompletedTask;
    }

    private void StopCore(bool scheduleConversion)
    {
        var outputPath = _outputPath;
        var temporaryWavPath = _temporaryWavPath;
        var restartCaptureAfterStop = _restartCaptureAfterStop;
        lock (_gate)
        {
            _finalFramesToWrite = CalculateFinalFrames(
                _recordingClock?.Elapsed ?? TimeSpan.Zero,
                MixedWaveFormat.SampleRate);
            _writerStopRequested = true;
        }

        StopCaptureUnlocked();
        lock (_gate)
        {
            _recordingActive = false;
            _captureStoppedForWriter = true;
        }

        try
        {
            _writerTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }

        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
            foreach (var source in _captureSources)
            {
                source.Buffer = null;
            }

            _sourceBuffers.Clear();
            _mixer = null;
            _recordingClock = null;
            _framesWritten = 0;
            _finalFramesToWrite = null;
            _writerStopRequested = false;
            _captureStoppedForWriter = false;
        }

        _writerCancellation?.Cancel();
        _writerCancellation?.Dispose();
        _writerCancellation = null;
        _writerTask = null;
        _outputPath = null;
        _temporaryWavPath = null;
        _restartCaptureAfterStop = false;

        if (restartCaptureAfterStop)
        {
            try
            {
                EnsureCaptureStarted();
            }
            catch
            {
                // Recording has already been finalized. Failing to re-prime devices must not
                // turn a saved recording into a failed stop operation.
            }
        }

        if (scheduleConversion &&
            !string.IsNullOrWhiteSpace(outputPath) &&
            !string.IsNullOrWhiteSpace(temporaryWavPath) &&
            File.Exists(temporaryWavPath))
        {
            StartBackgroundConversion(temporaryWavPath, outputPath);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        StopCapture();
    }

    private async Task RunWriterLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new float[MixedWaveFormat.SampleRate * MixedWaveFormat.Channels / 50];

        while (true)
        {
            var samplesToWrite = 0;
            var shouldStop = false;
            lock (_gate)
            {
                if (_writer is not null && _mixer is not null)
                {
                    var targetFrames = GetWriterTargetFrames();
                    samplesToWrite = CalculateSamplesToWrite(
                        targetFrames,
                        _framesWritten,
                        MixedWaveFormat.Channels,
                        buffer.Length);

                    if (samplesToWrite > 0)
                    {
                        var samplesRead = _mixer.Read(buffer, 0, samplesToWrite);
                        if (samplesRead > 0)
                        {
                            _writer.WriteSamples(buffer, 0, samplesRead);
                            _framesWritten += samplesRead / MixedWaveFormat.Channels;
                        }
                    }
                }

                shouldStop = ShouldStopWriter(
                    _writerStopRequested,
                    _captureStoppedForWriter,
                    _framesWritten,
                    _finalFramesToWrite);
            }

            if (shouldStop)
            {
                break;
            }

            if (samplesToWrite == 0)
            {
                await Task.Delay(5, cancellationToken);
            }
        }
    }

    private long GetWriterTargetFrames()
    {
        if (_writerStopRequested)
        {
            return _captureStoppedForWriter
                ? _finalFramesToWrite ?? _framesWritten
                : _framesWritten;
        }

        return CalculateDueFrames(
            _recordingClock?.Elapsed ?? TimeSpan.Zero,
            MixedWaveFormat.SampleRate,
            WriterLatency);
    }

    private void AddToBuffer(
        BufferedWaveProvider? target,
        byte[] buffer,
        int bytesRecorded,
        WaveFormat sourceFormat)
    {
        if (bytesRecorded <= 0)
        {
            return;
        }

        var converted = ConvertToMixedFormat(buffer, bytesRecorded, sourceFormat);
        lock (_gate)
        {
            if (_recordingActive && target is not null)
            {
                target.AddSamples(converted, 0, converted.Length);
            }
        }
    }

    private void EnsureCaptureStarted()
    {
        if (_captureActive)
        {
            return;
        }

        var startedSources = new List<CaptureSource>();
        var input = new WasapiCapture
        {
            WaveFormat = MixedWaveFormat
        };
        var inputSource = CreateCaptureSource(input, isInput: true);
        try
        {
            input.StartRecording();
            startedSources.Add(inputSource);
        }
        catch
        {
            input.Dispose();
            throw;
        }

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                WasapiLoopbackCapture? output = null;
                try
                {
                    output = new WasapiLoopbackCapture(device)
                    {
                        WaveFormat = MixedWaveFormat
                    };
                    var outputSource = CreateCaptureSource(output, isInput: false);
                    output.StartRecording();
                    startedSources.Add(outputSource);
                }
                catch
                {
                    // Some active render endpoints can be temporarily unavailable. Keep recording
                    // from the remaining devices instead of failing the whole meeting recording.
                    output?.Dispose();
                }
            }
        }
        catch
        {
            // Render endpoint enumeration can fail on restricted systems. Microphone capture
            // should still work, and the UI will continue showing system-output level as silent.
        }

        _captureSources.AddRange(startedSources);
        _captureActive = true;
    }

    private void StopCapture()
    {
        lock (_lifecycleGate)
        {
            StopCaptureUnlocked();
        }
    }

    private void StopCaptureUnlocked()
    {
        foreach (var source in _captureSources)
        {
            try
            {
                source.Capture.StopRecording();
            }
            catch
            {
                // Capture may already be stopped or disconnected; dispose below is enough.
            }
        }

        foreach (var source in _captureSources)
        {
            source.Capture.Dispose();
        }

        _captureSources.Clear();
        _lastInputPeak = 0;
        _lastOutputPeak = 0;
        _captureActive = false;
    }

    internal static BufferedWaveProvider CreateBufferedProviderForSource(WaveFormat waveFormat)
    {
        return new BufferedWaveProvider(waveFormat)
        {
            BufferDuration = TimeSpan.FromSeconds(30),
            DiscardOnBufferOverflow = false,
            ReadFully = true
        };
    }

    internal static bool ShouldStopWriter(
        bool stopRequested,
        BufferedWaveProvider? inputBuffer,
        BufferedWaveProvider? outputBuffer) =>
        stopRequested &&
        (inputBuffer?.BufferedBytes ?? 0) == 0 &&
        (outputBuffer?.BufferedBytes ?? 0) == 0;

    internal static bool ShouldStopWriter(
        bool stopRequested,
        bool captureStopped,
        long framesWritten,
        long? finalFramesToWrite) =>
        stopRequested &&
        captureStopped &&
        finalFramesToWrite.HasValue &&
        framesWritten >= finalFramesToWrite.Value;

    internal static long CalculateDueFrames(TimeSpan elapsed, int sampleRate, TimeSpan latency)
    {
        var effectiveTicks = Math.Max(0, elapsed.Ticks - latency.Ticks);
        return effectiveTicks * sampleRate / TimeSpan.TicksPerSecond;
    }

    internal static long CalculateFinalFrames(TimeSpan elapsed, int sampleRate)
    {
        var effectiveTicks = Math.Max(0, elapsed.Ticks);
        return effectiveTicks * sampleRate / TimeSpan.TicksPerSecond;
    }

    internal static int CalculateSamplesToWrite(
        long targetFrames,
        long framesWritten,
        int channels,
        int maxSamples)
    {
        var framesToWrite = Math.Max(0, targetFrames - framesWritten);
        var maxFrames = maxSamples / channels;
        return (int)Math.Min(framesToWrite, maxFrames) * channels;
    }

    internal static byte[] ConvertToMixedFormat(byte[] buffer, int bytesRecorded, WaveFormat sourceFormat)
    {
        if (IsSameFormat(sourceFormat, MixedWaveFormat))
        {
            var exact = new byte[bytesRecorded];
            Buffer.BlockCopy(buffer, 0, exact, 0, bytesRecorded);
            return exact;
        }

        using var sourceStream = new RawSourceWaveStream(new MemoryStream(buffer, 0, bytesRecorded), sourceFormat);
        using var resampler = new MediaFoundationResampler(sourceStream, MixedWaveFormat)
        {
            ResamplerQuality = 60
        };
        using var converted = new MemoryStream();
        var readBuffer = new byte[MixedWaveFormat.AverageBytesPerSecond / 10];
        int bytesRead;

        while ((bytesRead = resampler.Read(readBuffer, 0, readBuffer.Length)) > 0)
        {
            converted.Write(readBuffer, 0, bytesRead);
        }

        return converted.ToArray();
    }

    internal static string GetTemporaryWavPath(string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath);
        var fileName = Path.GetFileNameWithoutExtension(outputPath);
        return Path.Combine(directory ?? "", $"{fileName}.recording.wav");
    }

    internal static string GetTemporaryMp3Path(string outputPath) => $"{outputPath}.tmp";

    internal static string GetEncodingMp3Path(string outputPath) => $"{outputPath}.encoding.mp3";

    internal static string GetOutputPathFromTemporaryWavPath(string temporaryWavPath)
    {
        const string suffix = ".recording.wav";
        if (!temporaryWavPath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Temporary WAV path must end with .recording.wav.", nameof(temporaryWavPath));
        }

        return string.Concat(temporaryWavPath.AsSpan(0, temporaryWavPath.Length - suffix.Length), ".mp3");
    }

    internal static string FinalizeTemporaryWavToMp3(string temporaryWavPath, string outputPath)
    {
        if (!File.Exists(temporaryWavPath))
        {
            throw new FileNotFoundException("Temporary WAV file was not found.", temporaryWavPath);
        }

        var encodingMp3Path = GetEncodingMp3Path(outputPath);
        var temporaryMp3Path = GetTemporaryMp3Path(outputPath);
        DeleteIfExists(encodingMp3Path);
        DeleteIfExists(temporaryMp3Path);

        try
        {
            EncodeTemporaryWavToMp3(temporaryWavPath, encodingMp3Path);
            if (!File.Exists(encodingMp3Path) || new FileInfo(encodingMp3Path).Length == 0)
            {
                throw new InvalidOperationException("MP3 encoder did not create a usable file.");
            }

            File.Move(encodingMp3Path, temporaryMp3Path);
            var finalPath = GetAvailableOutputPath(outputPath);
            File.Move(temporaryMp3Path, finalPath);
            File.Delete(temporaryWavPath);
            return finalPath;
        }
        catch
        {
            DeleteIfExists(encodingMp3Path);
            DeleteIfExists(temporaryMp3Path);
            throw;
        }
    }

    public static async Task RecoverPendingConversionsAsync(
        string outputFolder,
        Action<AudioFileSavedEventArgs> onSaved,
        Action<AudioFileSaveFailedEventArgs> onFailed,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(outputFolder))
        {
            return;
        }

        foreach (var temporaryWavPath in Directory.EnumerateFiles(outputFolder, "*.recording.wav"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outputPath = GetOutputPathFromTemporaryWavPath(temporaryWavPath);
            try
            {
                var savedPath = await Task.Run(
                    () => FinalizeTemporaryWavToMp3(temporaryWavPath, outputPath),
                    CancellationToken.None);
                onSaved(new AudioFileSavedEventArgs(outputPath, savedPath, temporaryWavPath));
            }
            catch (Exception ex)
            {
                onFailed(new AudioFileSaveFailedEventArgs(outputPath, temporaryWavPath, ex));
            }
        }
    }

    private void StartBackgroundConversion(string temporaryWavPath, string outputPath)
    {
        _ = Task.Run(() =>
        {
            try
            {
                var savedPath = FinalizeTemporaryWavToMp3(temporaryWavPath, outputPath);
                FileSaved?.Invoke(this, new AudioFileSavedEventArgs(outputPath, savedPath, temporaryWavPath));
            }
            catch (Exception ex)
            {
                FileSaveFailed?.Invoke(this, new AudioFileSaveFailedEventArgs(outputPath, temporaryWavPath, ex));
            }
        });
    }

    internal static void EncodeTemporaryWavToMp3(string temporaryWavPath, string outputPath)
    {
        using (var reader = new WaveFileReader(temporaryWavPath))
        {
            MediaFoundationEncoder.EncodeToMp3(reader, outputPath, Mp3Bitrate);
        }
    }

    private static string GetAvailableOutputPath(string requestedOutputPath)
    {
        if (!File.Exists(requestedOutputPath))
        {
            return requestedOutputPath;
        }

        var directory = Path.GetDirectoryName(requestedOutputPath) ?? "";
        var fileName = Path.GetFileNameWithoutExtension(requestedOutputPath);
        var extension = Path.GetExtension(requestedOutputPath);

        for (var index = 2; ; index++)
        {
            var candidate = Path.Combine(directory, $"{fileName} ({index}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static bool IsSameFormat(WaveFormat left, WaveFormat right)
    {
        var normalizedLeft = left is WaveFormatExtensible extensibleLeft
            ? extensibleLeft.ToStandardWaveFormat()
            : left;
        var normalizedRight = right is WaveFormatExtensible extensibleRight
            ? extensibleRight.ToStandardWaveFormat()
            : right;

        return normalizedLeft.Encoding == normalizedRight.Encoding &&
            normalizedLeft.SampleRate == normalizedRight.SampleRate &&
            normalizedLeft.Channels == normalizedRight.Channels &&
            normalizedLeft.BitsPerSample == normalizedRight.BitsPerSample;
    }

    internal static float CalculatePeak(byte[] buffer, int bytesRecorded, WaveFormat waveFormat)
    {
        var max = 0f;
        var format = waveFormat is WaveFormatExtensible extensible
            ? extensible.ToStandardWaveFormat()
            : waveFormat;

        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            for (var index = 0; index + 3 < bytesRecorded; index += 4)
            {
                var sample = BitConverter.ToSingle(buffer, index);
                if (!float.IsNaN(sample))
                {
                    max = Math.Max(max, Math.Abs(sample));
                }
            }

            return max;
        }

        if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
        {
            for (var index = 0; index + 1 < bytesRecorded; index += 2)
            {
                var sample = BitConverter.ToInt16(buffer, index) / 32768f;
                max = Math.Max(max, Math.Abs(sample));
            }

            return max;
        }

        if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 24)
        {
            for (var index = 0; index + 2 < bytesRecorded; index += 3)
            {
                var sample = buffer[index] | (buffer[index + 1] << 8) | (buffer[index + 2] << 16);
                if ((sample & 0x800000) != 0)
                {
                    sample |= unchecked((int)0xFF000000);
                }

                max = Math.Max(max, Math.Abs(sample / 8388608f));
            }

            return max;
        }

        if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 32)
        {
            for (var index = 0; index + 3 < bytesRecorded; index += 4)
            {
                var sample = BitConverter.ToInt32(buffer, index) / 2147483648f;
                max = Math.Max(max, Math.Abs(sample));
            }

            return max;
        }

        return ContainsNonZeroByte(buffer, bytesRecorded) ? 1f : 0f;
    }

    private static bool ContainsNonZeroByte(byte[] buffer, int bytesRecorded)
    {
        for (var index = 0; index < bytesRecorded; index++)
        {
            if (buffer[index] != 0)
            {
                return true;
            }
        }

        return false;
    }

    private CaptureSource CreateCaptureSource(WasapiCapture capture, bool isInput)
    {
        var source = new CaptureSource(capture, capture.WaveFormat, isInput);
        capture.DataAvailable += (_, e) =>
        {
            AddToBuffer(source.Buffer, e.Buffer, e.BytesRecorded, source.SourceFormat);
            var peak = CalculatePeak(e.Buffer, e.BytesRecorded, source.SourceFormat);
            if (source.IsInput)
            {
                _lastInputPeak = peak;
            }
            else
            {
                source.LastPeak = peak;
                _lastOutputPeak = _captureSources
                    .Where(captureSource => !captureSource.IsInput)
                    .Select(captureSource => captureSource.LastPeak)
                    .DefaultIfEmpty(0)
                    .Max();
            }

            LevelChanged?.Invoke(this, new AudioLevel(_lastInputPeak, _lastOutputPeak));
        };

        return source;
    }

    private sealed class CaptureSource(WasapiCapture capture, WaveFormat sourceFormat, bool isInput)
    {
        public WasapiCapture Capture { get; } = capture;
        public WaveFormat SourceFormat { get; } = sourceFormat;
        public bool IsInput { get; } = isInput;
        public float LastPeak { get; set; }
        public BufferedWaveProvider? Buffer { get; set; }
    }
}
