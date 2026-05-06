namespace Autorecord.Core.Audio;

public interface IAudioRecorder : IAsyncDisposable
{
    event EventHandler<AudioLevel>? LevelChanged;

    Task StartAsync(string outputPath, CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}
