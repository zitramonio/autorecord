using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Autorecord.Core.Tests")]

namespace Autorecord.Core.Audio;

public sealed class NaudioWavRecorder : IAudioRecorder
{
    private static readonly WaveFormat MixedWaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);
    private static readonly WaveFormat TemporaryWaveFormat = new(48_000, 16, 2);
    private const int Mp3Bitrate = 128_000;

    private WasapiCapture? _input;
    private WasapiLoopbackCapture? _output;
    private BufferedWaveProvider? _inputBuffer;
    private BufferedWaveProvider? _outputBuffer;
    private MixingSampleProvider? _mixer;
    private WaveFileWriter? _writer;
    private CancellationTokenSource? _writerCancellation;
    private Task? _writerTask;
    private string? _outputPath;
    private string? _temporaryWavPath;
    private readonly object _gate = new();
    private readonly object _lifecycleGate = new();
    private float _lastInputPeak;
    private float _lastOutputPeak;
    private bool _captureActive;
    private bool _recordingActive;
    private bool _writerStopRequested;

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
                EnsureCaptureStarted();
                _inputBuffer = CreateBufferedProviderForSource(MixedWaveFormat);
                _outputBuffer = CreateBufferedProviderForSource(MixedWaveFormat);
                _mixer = new MixingSampleProvider(
                [
                    new WaveToSampleProvider(_inputBuffer),
                    new WaveToSampleProvider(_outputBuffer)
                ])
                {
                    ReadFully = true
                };
                _writer = new WaveFileWriter(_temporaryWavPath, TemporaryWaveFormat);
                _writerCancellation = new CancellationTokenSource();
                _writerStopRequested = false;
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
        _recordingActive = false;
        lock (_gate)
        {
            _writerStopRequested = true;
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
            _inputBuffer = null;
            _outputBuffer = null;
            _mixer = null;
            _writerStopRequested = false;
        }

        _writerCancellation?.Cancel();
        _writerCancellation?.Dispose();
        _writerCancellation = null;
        _writerTask = null;
        _outputPath = null;
        _temporaryWavPath = null;

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
            var shouldStop = false;
            lock (_gate)
            {
                if (_writer is not null && _mixer is not null)
                {
                    var samplesRead = _mixer.Read(buffer, 0, buffer.Length);
                    if (samplesRead > 0)
                    {
                        _writer.WriteSamples(buffer, 0, samplesRead);
                    }
                }

                shouldStop = ShouldStopWriter(_writerStopRequested, _inputBuffer, _outputBuffer);
            }

            if (shouldStop)
            {
                break;
            }

            await Task.Delay(20, cancellationToken);
        }
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

        _input = new WasapiCapture
        {
            WaveFormat = MixedWaveFormat
        };
        _output = new WasapiLoopbackCapture
        {
            WaveFormat = MixedWaveFormat
        };
        var inputWaveFormat = _input.WaveFormat;
        var outputWaveFormat = _output.WaveFormat;

        _input.DataAvailable += (_, e) =>
        {
            AddToBuffer(_inputBuffer, e.Buffer, e.BytesRecorded, inputWaveFormat);
            _lastInputPeak = CalculatePeak(e.Buffer, e.BytesRecorded, inputWaveFormat);
            LevelChanged?.Invoke(this, new AudioLevel(_lastInputPeak, _lastOutputPeak));
        };

        _output.DataAvailable += (_, e) =>
        {
            AddToBuffer(_outputBuffer, e.Buffer, e.BytesRecorded, outputWaveFormat);
            _lastOutputPeak = CalculatePeak(e.Buffer, e.BytesRecorded, outputWaveFormat);
            LevelChanged?.Invoke(this, new AudioLevel(_lastInputPeak, _lastOutputPeak));
        };

        _input.StartRecording();
        _output.StartRecording();
        _captureActive = true;
    }

    private void StopCapture()
    {
        lock (_lifecycleGate)
        {
            _input?.StopRecording();
            _output?.StopRecording();
            _input?.Dispose();
            _output?.Dispose();
            _input = null;
            _output = null;
            _captureActive = false;
        }
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
}
