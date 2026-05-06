using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Autorecord.Core.Audio;

public sealed class NaudioWavRecorder : IAudioRecorder
{
    private WasapiCapture? _input;
    private WasapiLoopbackCapture? _output;
    private WaveFileWriter? _writer;
    private readonly object _gate = new();
    private float _lastInputPeak;
    private float _lastOutputPeak;

    public event EventHandler<AudioLevel>? LevelChanged;

    public Task StartAsync(string outputPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        _input = new WasapiCapture();
        _output = new WasapiLoopbackCapture();
        _writer = new WaveFileWriter(outputPath, _output.WaveFormat);

        _input.DataAvailable += (_, e) =>
        {
            _lastInputPeak = CalculatePeak(e.Buffer, e.BytesRecorded, _input.WaveFormat);
            LevelChanged?.Invoke(this, new AudioLevel(_lastInputPeak, _lastOutputPeak));
        };

        _output.DataAvailable += (_, e) =>
        {
            lock (_gate)
            {
                _writer?.Write(e.Buffer, 0, e.BytesRecorded);
            }

            _lastOutputPeak = CalculatePeak(e.Buffer, e.BytesRecorded, _output.WaveFormat);
            LevelChanged?.Invoke(this, new AudioLevel(_lastInputPeak, _lastOutputPeak));
        };

        _input.StartRecording();
        _output.StartRecording();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _input?.StopRecording();
        _output?.StopRecording();

        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _input?.Dispose();
        _output?.Dispose();
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
