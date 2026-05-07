namespace Autorecord.Core.Audio;

public sealed class AudioFileSavedEventArgs : EventArgs
{
    public AudioFileSavedEventArgs(string requestedOutputPath, string savedOutputPath, string temporaryWavPath)
    {
        RequestedOutputPath = requestedOutputPath;
        SavedOutputPath = savedOutputPath;
        TemporaryWavPath = temporaryWavPath;
    }

    public string RequestedOutputPath { get; }
    public string SavedOutputPath { get; }
    public string TemporaryWavPath { get; }
}
