namespace Autorecord.Core.Audio;

public sealed class AudioFileSaveFailedEventArgs : EventArgs
{
    public AudioFileSaveFailedEventArgs(string requestedOutputPath, string temporaryWavPath, Exception error)
    {
        RequestedOutputPath = requestedOutputPath;
        TemporaryWavPath = temporaryWavPath;
        Error = error;
    }

    public string RequestedOutputPath { get; }
    public string TemporaryWavPath { get; }
    public Exception Error { get; }
}
