using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Autorecord.Core.Audio;

internal interface IAudioCaptureSession : IDisposable
{
    event EventHandler<WaveInEventArgs>? DataAvailable;

    WaveFormat WaveFormat { get; }

    void StartRecording();

    void StopRecording();
}

internal interface IAudioCaptureSessionFactory
{
    IAudioCaptureSession CreateInput(WaveFormat waveFormat);

    IReadOnlyList<IAudioCaptureSession> CreateRenderLoopbacks();
}

internal sealed class NaudioCaptureSessionFactory : IAudioCaptureSessionFactory
{
    public IAudioCaptureSession CreateInput(WaveFormat waveFormat)
    {
        var capture = new WasapiCapture
        {
            WaveFormat = waveFormat
        };

        return new WasapiAudioCaptureSession(capture);
    }

    public IReadOnlyList<IAudioCaptureSession> CreateRenderLoopbacks()
    {
        var sessions = new List<IAudioCaptureSession>();
        using var enumerator = new MMDeviceEnumerator();
        foreach (var device in GetDefaultRenderDevices(enumerator))
        {
            try
            {
                sessions.Add(new WasapiAudioCaptureSession(new WasapiLoopbackCapture(device)));
            }
            catch
            {
                // A default render endpoint can be temporarily unavailable.
            }
        }

        return sessions;
    }

    private static IReadOnlyList<MMDevice> GetDefaultRenderDevices(MMDeviceEnumerator enumerator)
    {
        var devices = new List<MMDevice>();
        var deviceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddDefaultRenderDevice(enumerator, Role.Console, devices, deviceIds);
        AddDefaultRenderDevice(enumerator, Role.Multimedia, devices, deviceIds);
        AddDefaultRenderDevice(enumerator, Role.Communications, devices, deviceIds);
        return devices;
    }

    private static void AddDefaultRenderDevice(
        MMDeviceEnumerator enumerator,
        Role role,
        List<MMDevice> devices,
        HashSet<string> deviceIds)
    {
        try
        {
            var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, role);
            if (deviceIds.Add(device.ID))
            {
                devices.Add(device);
            }
        }
        catch
        {
            // Windows may have no default endpoint for a role while audio devices are changing.
        }
    }
}

internal sealed class WasapiAudioCaptureSession(WasapiCapture capture) : IAudioCaptureSession
{
    private readonly WasapiCapture _capture = capture;

    public event EventHandler<WaveInEventArgs>? DataAvailable
    {
        add => _capture.DataAvailable += value;
        remove => _capture.DataAvailable -= value;
    }

    public WaveFormat WaveFormat => _capture.WaveFormat;

    public void StartRecording()
    {
        _capture.StartRecording();
    }

    public void StopRecording()
    {
        _capture.StopRecording();
    }

    public void Dispose()
    {
        _capture.Dispose();
    }
}
