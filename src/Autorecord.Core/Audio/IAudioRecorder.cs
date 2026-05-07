namespace Autorecord.Core.Audio;

public interface IAudioRecorder : IAsyncDisposable
{
    event EventHandler<AudioLevel>? LevelChanged;
    event EventHandler<AudioFileSavedEventArgs>? FileSaved;
    event EventHandler<AudioFileSaveFailedEventArgs>? FileSaveFailed;

    Task PrepareAsync(CancellationToken cancellationToken);

    Task StartAsync(string outputPath, CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}
