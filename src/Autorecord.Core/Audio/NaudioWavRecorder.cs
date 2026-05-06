using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Autorecord.Core.Audio;

public sealed class NaudioWavRecorder : IAudioRecorder
{
    private static readonly WaveFormat MixedWaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);

    private WasapiCapture? _input;
    private WasapiLoopbackCapture? _output;
    private BufferedWaveProvider? _inputBuffer;
    private BufferedWaveProvider? _outputBuffer;
    private MixingSampleProvider? _mixer;
    private WaveFileWriter? _writer;
    private CancellationTokenSource? _writerCancellation;
    private Task? _writerTask;
    private readonly object _gate = new();
    private float _lastInputPeak;
    private float _lastOutputPeak;
    private bool _recordingActive;

    public event EventHandler<AudioLevel>? LevelChanged;

    public Task StartAsync(string outputPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        _input = new WasapiCapture();
        _output = new WasapiLoopbackCapture();
        var inputWaveFormat = _input.WaveFormat;
        var outputWaveFormat = _output.WaveFormat;
        _inputBuffer = CreateBufferedProvider();
        _outputBuffer = CreateBufferedProvider();
        _mixer = new MixingSampleProvider(
        [
            new WaveToSampleProvider(_inputBuffer),
            new WaveToSampleProvider(_outputBuffer)
        ])
        {
            ReadFully = true
        };
        _writer = new WaveFileWriter(outputPath, MixedWaveFormat);
        _writerCancellation = new CancellationTokenSource();
        _recordingActive = true;
        _writerTask = Task.Run(() => RunWriterLoopAsync(_writerCancellation.Token), CancellationToken.None);

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
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _recordingActive = false;
        _input?.StopRecording();
        _output?.StopRecording();
        _input?.Dispose();
        _output?.Dispose();
        _input = null;
        _output = null;
        _writerCancellation?.Cancel();

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
        }

        _writerCancellation?.Dispose();
        _writerCancellation = null;
        _writerTask = null;

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _input?.Dispose();
        _output?.Dispose();
    }

    private async Task RunWriterLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new float[MixedWaveFormat.SampleRate * MixedWaveFormat.Channels / 50];

        while (!cancellationToken.IsCancellationRequested)
        {
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
            }

            await Task.Delay(20, cancellationToken);
        }
    }

    private void AddToBuffer(BufferedWaveProvider? target, byte[] buffer, int bytesRecorded, WaveFormat sourceFormat)
    {
        if (!_recordingActive || target is null || bytesRecorded <= 0)
        {
            return;
        }

        var converted = ConvertToMixedFormat(buffer, bytesRecorded, sourceFormat);
        lock (_gate)
        {
            if (_recordingActive)
            {
                target.AddSamples(converted, 0, converted.Length);
            }
        }
    }

    private static BufferedWaveProvider CreateBufferedProvider()
    {
        return new BufferedWaveProvider(MixedWaveFormat)
        {
            BufferDuration = TimeSpan.FromSeconds(5),
            DiscardOnBufferOverflow = true,
            ReadFully = true
        };
    }

    private static byte[] ConvertToMixedFormat(byte[] buffer, int bytesRecorded, WaveFormat sourceFormat)
    {
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

    private static float CalculatePeak(byte[] buffer, int bytesRecorded, WaveFormat waveFormat)
    {
        var max = 0f;

        if (waveFormat.Encoding == WaveFormatEncoding.IeeeFloat && waveFormat.BitsPerSample == 32)
        {
            for (var index = 0; index + 3 < bytesRecorded; index += 4)
            {
                var sample = BitConverter.ToSingle(buffer, index);
                max = Math.Max(max, Math.Abs(sample));
            }

            return max;
        }

        if (waveFormat.BitsPerSample == 16)
        {
            for (var index = 0; index + 1 < bytesRecorded; index += 2)
            {
                var sample = BitConverter.ToInt16(buffer, index) / 32768f;
                max = Math.Max(max, Math.Abs(sample));
            }
        }

        return max;
    }
}
