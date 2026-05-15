namespace Autorecord.Core.Audio;

public sealed class AudioFileSavedEventArgs : EventArgs
{
    public AudioFileSavedEventArgs(
        string requestedOutputPath,
        string savedOutputPath,
        string temporaryWavPath,
        string? microphoneTrackPath = null,
        string? systemTrackPath = null)
    {
        RequestedOutputPath = requestedOutputPath;
        SavedOutputPath = savedOutputPath;
        TemporaryWavPath = temporaryWavPath;
        MicrophoneTrackPath = microphoneTrackPath;
        SystemTrackPath = systemTrackPath;
    }

    public string RequestedOutputPath { get; }
    public string SavedOutputPath { get; }
    public string TemporaryWavPath { get; }
    public string? MicrophoneTrackPath { get; }
    public string? SystemTrackPath { get; }
}
