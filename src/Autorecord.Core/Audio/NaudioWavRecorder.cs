using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Diagnostics;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Autorecord.Core.Tests")]

namespace Autorecord.Core.Audio;

public sealed class NaudioWavRecorder : IAudioRecorder
{
    private const string MicrophoneTrackName = "mic";
    private const string SystemTrackName = "system";
    private static readonly WaveFormat MixedWaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);
    private static readonly WaveFormat TemporaryWaveFormat = new(48_000, 16, 2);
    private static readonly TimeSpan WriterLatency = TimeSpan.FromMilliseconds(200);
    private const int Mp3Bitrate = 128_000;

    private readonly IAudioCaptureSessionFactory _captureSessionFactory;
    private readonly List<CaptureSource> _captureSources = [];
    private readonly List<BufferedWaveProvider> _sourceBuffers = [];
    private readonly List<BufferedWaveProvider> _microphoneTrackBuffers = [];
    private readonly List<BufferedWaveProvider> _systemTrackBuffers = [];
    private MixingSampleProvider? _mixer;
    private MixingSampleProvider? _microphoneTrackMixer;
    private MixingSampleProvider? _systemTrackMixer;
    private WaveFileWriter? _writer;
    private WaveFileWriter? _microphoneTrackWriter;
    private WaveFileWriter? _systemTrackWriter;
    private CancellationTokenSource? _writerCancellation;
    private Task? _writerTask;
    private Stopwatch? _recordingClock;
    private string? _outputPath;
    private string? _temporaryWavPath;
    private string? _temporaryMicrophoneWavPath;
    private string? _temporarySystemWavPath;
    private readonly object _gate = new();
    private readonly object _lifecycleGate = new();
    private float _lastInputPeak;
    private float _lastOutputPeak;
    private long _framesWritten;
    private long _microphoneFramesWritten;
    private long _systemFramesWritten;
    private long? _finalFramesToWrite;
    private bool _captureActive;
    private bool _recordingActive;
    private bool _restartCaptureAfterStop;
    private bool _writerStopRequested;
    private bool _captureStoppedForWriter;

    public event EventHandler<AudioLevel>? LevelChanged;
    public event EventHandler<AudioFileSavedEventArgs>? FileSaved;
    public event EventHandler<AudioFileSaveFailedEventArgs>? FileSaveFailed;

    public NaudioWavRecorder()
        : this(new NaudioCaptureSessionFactory())
    {
    }

    internal NaudioWavRecorder(IAudioCaptureSessionFactory captureSessionFactory)
    {
        _captureSessionFactory = captureSessionFactory;
    }

    public Task PrepareAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lifecycleGate)
        {
            EnsureCaptureStarted(includeRenderDevices: false);
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
                _temporaryMicrophoneWavPath = GetTemporaryTechnicalTrackPath(outputPath, MicrophoneTrackName);
                _temporarySystemWavPath = GetTemporaryTechnicalTrackPath(outputPath, SystemTrackName);
                _restartCaptureAfterStop = _captureActive;
                EnsureCaptureStarted(includeRenderDevices: true);
                _sourceBuffers.Clear();
                _microphoneTrackBuffers.Clear();
                _systemTrackBuffers.Clear();
                foreach (var source in _captureSources)
                {
                    source.Buffer = CreateBufferedProviderForSource(MixedWaveFormat);
                    _sourceBuffers.Add(source.Buffer);
                    source.TechnicalTrackBuffer = CreateBufferedProviderForSource(MixedWaveFormat);
                    if (source.IsInput)
                    {
                        _microphoneTrackBuffers.Add(source.TechnicalTrackBuffer);
                    }
                    else
                    {
                        _systemTrackBuffers.Add(source.TechnicalTrackBuffer);
                    }
                }

                _mixer = CreateMixer(_sourceBuffers);
                _microphoneTrackMixer = CreateMixer(_microphoneTrackBuffers);
                _systemTrackMixer = _systemTrackBuffers.Count == 0 ? null : CreateMixer(_systemTrackBuffers);
                _writer = new WaveFileWriter(_temporaryWavPath, TemporaryWaveFormat);
                _microphoneTrackWriter = new WaveFileWriter(_temporaryMicrophoneWavPath, TemporaryWaveFormat);
                _systemTrackWriter = new WaveFileWriter(_temporarySystemWavPath, TemporaryWaveFormat);
                _writerCancellation = new CancellationTokenSource();
                _recordingClock = Stopwatch.StartNew();
                _framesWritten = 0;
                _microphoneFramesWritten = 0;
                _systemFramesWritten = 0;
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
            _microphoneTrackWriter?.Dispose();
            _microphoneTrackWriter = null;
            _systemTrackWriter?.Dispose();
            _systemTrackWriter = null;
            foreach (var source in _captureSources)
            {
                source.Buffer = null;
                source.TechnicalTrackBuffer = null;
            }

            _sourceBuffers.Clear();
            _microphoneTrackBuffers.Clear();
            _systemTrackBuffers.Clear();
            _mixer = null;
            _microphoneTrackMixer = null;
            _systemTrackMixer = null;
            _recordingClock = null;
            _framesWritten = 0;
            _microphoneFramesWritten = 0;
            _systemFramesWritten = 0;
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
        _temporaryMicrophoneWavPath = null;
        _temporarySystemWavPath = null;
        _restartCaptureAfterStop = false;

        if (restartCaptureAfterStop)
        {
            try
            {
                EnsureCaptureStarted(includeRenderDevices: false);
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
        var microphoneBuffer = new float[buffer.Length];
        var systemBuffer = new float[buffer.Length];

        while (true)
        {
            var samplesToWrite = 0;
            var shouldStop = false;
            lock (_gate)
            {
                if (_writer is not null && _mixer is not null)
                {
                    var targetFrames = GetWriterTargetFrames();
                    samplesToWrite = Math.Max(
                        samplesToWrite,
                        WriteDueSamples(_writer, _mixer, targetFrames, ref _framesWritten, buffer));
                    if (_microphoneTrackWriter is not null)
                    {
                        samplesToWrite = Math.Max(
                            samplesToWrite,
                            WriteDueSamples(
                                _microphoneTrackWriter,
                                _microphoneTrackMixer,
                                targetFrames,
                                ref _microphoneFramesWritten,
                                microphoneBuffer));
                    }

                    if (_systemTrackWriter is not null)
                    {
                        samplesToWrite = Math.Max(
                            samplesToWrite,
                            WriteDueSamples(
                                _systemTrackWriter,
                                _systemTrackMixer,
                                targetFrames,
                                ref _systemFramesWritten,
                                systemBuffer));
                    }
                }

                shouldStop = ShouldStopWriter(
                    _writerStopRequested,
                    _captureStoppedForWriter,
                    _framesWritten,
                    _finalFramesToWrite) &&
                    TechnicalTrackReachedFinalFrame(_microphoneFramesWritten, _finalFramesToWrite) &&
                    TechnicalTrackReachedFinalFrame(_systemFramesWritten, _finalFramesToWrite);
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

    private static MixingSampleProvider CreateMixer(IReadOnlyList<BufferedWaveProvider> buffers)
    {
        return new MixingSampleProvider(buffers.Select(buffer => new WaveToSampleProvider(buffer)))
        {
            ReadFully = true
        };
    }

    private static int WriteDueSamples(
        WaveFileWriter writer,
        MixingSampleProvider? mixer,
        long targetFrames,
        ref long framesWritten,
        float[] buffer)
    {
        var samplesToWrite = CalculateSamplesToWrite(
            targetFrames,
            framesWritten,
            MixedWaveFormat.Channels,
            buffer.Length);
        if (samplesToWrite <= 0)
        {
            return 0;
        }

        var samplesRead = samplesToWrite;
        if (mixer is null)
        {
            Array.Clear(buffer, 0, samplesToWrite);
        }
        else
        {
            samplesRead = mixer.Read(buffer, 0, samplesToWrite);
        }

        if (samplesRead > 0)
        {
            writer.WriteSamples(buffer, 0, samplesRead);
            framesWritten += samplesRead / MixedWaveFormat.Channels;
        }

        return samplesToWrite;
    }

    private static bool TechnicalTrackReachedFinalFrame(long framesWritten, long? finalFramesToWrite)
    {
        return finalFramesToWrite.HasValue && framesWritten >= finalFramesToWrite.Value;
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
        BufferedWaveProvider? technicalTrackTarget,
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
                technicalTrackTarget?.AddSamples(converted, 0, converted.Length);
            }
        }
    }

    private void EnsureCaptureStarted(bool includeRenderDevices)
    {
        if (_captureSources.All(source => !source.IsInput))
        {
            StartInputCaptureSource();
        }

        if (includeRenderDevices && _captureSources.All(source => source.IsInput))
        {
            StartRenderCaptureSources();
        }

        _captureActive = _captureSources.Count > 0;
    }

    private void StartInputCaptureSource()
    {
        var input = _captureSessionFactory.CreateInput(MixedWaveFormat);
        var inputSource = CreateCaptureSource(input, isInput: true);
        try
        {
            input.StartRecording();
            _captureSources.Add(inputSource);
        }
        catch
        {
            input.Dispose();
            throw;
        }
    }

    private void StartRenderCaptureSources()
    {
        IReadOnlyList<IAudioCaptureSession> renderCaptures;
        try
        {
            renderCaptures = _captureSessionFactory.CreateRenderLoopbacks();
        }
        catch
        {
            // Render endpoint enumeration can fail on restricted systems. Microphone capture
            // should still work, and the UI will continue showing system-output level as silent.
            return;
        }

        foreach (var output in renderCaptures)
        {
            try
            {
                var outputSource = CreateCaptureSource(output, isInput: false);
                output.StartRecording();
                _captureSources.Add(outputSource);
            }
            catch
            {
                // Some active render endpoints can be temporarily unavailable. Keep recording
                // from the remaining devices instead of failing the whole meeting recording.
                output.Dispose();
            }
        }
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

    internal static string GetTemporaryTechnicalTrackPath(string outputPath, string trackName)
    {
        var directory = Path.GetDirectoryName(outputPath);
        var fileName = Path.GetFileNameWithoutExtension(outputPath);
        return Path.Combine(directory ?? "", $"{fileName}.{trackName}.recording.wav");
    }

    internal static string GetFinalTechnicalTrackPath(string outputPath, string trackName)
    {
        var directory = Path.GetDirectoryName(outputPath);
        var fileName = Path.GetFileNameWithoutExtension(outputPath);
        return Path.Combine(directory ?? "", $"{fileName}.{trackName}.wav");
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
            FinalizeTechnicalTrackWav(outputPath, finalPath, "mic");
            FinalizeTechnicalTrackWav(outputPath, finalPath, "system");
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
                onSaved(CreateAudioFileSavedEventArgs(outputPath, savedPath, temporaryWavPath));
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
                FileSaved?.Invoke(this, CreateAudioFileSavedEventArgs(outputPath, savedPath, temporaryWavPath));
            }
            catch (Exception ex)
            {
                FileSaveFailed?.Invoke(this, new AudioFileSaveFailedEventArgs(outputPath, temporaryWavPath, ex));
            }
        });
    }

    internal static void EncodeTemporaryWavToMp3(string temporaryWavPath, string outputPath)
    {
        _ = WavFileRepair.TryRepairInPlace(temporaryWavPath);
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

    private static AudioFileSavedEventArgs CreateAudioFileSavedEventArgs(
        string requestedOutputPath,
        string savedOutputPath,
        string temporaryWavPath)
    {
        var microphoneTrackPath = GetFinalTechnicalTrackPath(savedOutputPath, "mic");
        var systemTrackPath = GetFinalTechnicalTrackPath(savedOutputPath, "system");
        return new AudioFileSavedEventArgs(
            requestedOutputPath,
            savedOutputPath,
            temporaryWavPath,
            File.Exists(microphoneTrackPath) ? microphoneTrackPath : null,
            File.Exists(systemTrackPath) ? systemTrackPath : null);
    }

    private static void FinalizeTechnicalTrackWav(string requestedOutputPath, string savedOutputPath, string trackName)
    {
        var temporaryTrackPath = GetTemporaryTechnicalTrackPath(requestedOutputPath, trackName);
        if (!File.Exists(temporaryTrackPath))
        {
            return;
        }

        _ = WavFileRepair.TryRepairInPlace(temporaryTrackPath);
        var finalTrackPath = GetFinalTechnicalTrackPath(savedOutputPath, trackName);
        finalTrackPath = GetAvailableOutputPath(finalTrackPath);
        File.Move(temporaryTrackPath, finalTrackPath);
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

    private CaptureSource CreateCaptureSource(IAudioCaptureSession capture, bool isInput)
    {
        var source = new CaptureSource(capture, capture.WaveFormat, isInput);
        capture.DataAvailable += (_, e) =>
        {
            AddToBuffer(source.Buffer, source.TechnicalTrackBuffer, e.Buffer, e.BytesRecorded, source.SourceFormat);
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

    private sealed class CaptureSource(IAudioCaptureSession capture, WaveFormat sourceFormat, bool isInput)
    {
        public IAudioCaptureSession Capture { get; } = capture;
        public WaveFormat SourceFormat { get; } = sourceFormat;
        public bool IsInput { get; } = isInput;
        public float LastPeak { get; set; }
        public BufferedWaveProvider? Buffer { get; set; }
        public BufferedWaveProvider? TechnicalTrackBuffer { get; set; }
    }
}
