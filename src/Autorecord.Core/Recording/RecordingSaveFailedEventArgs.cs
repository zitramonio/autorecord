namespace Autorecord.Core.Recording;

public sealed class RecordingSaveFailedEventArgs : EventArgs
{
    public RecordingSaveFailedEventArgs(RecordingSession session, string temporaryWavPath, Exception error)
    {
        Session = session;
        TemporaryWavPath = temporaryWavPath;
        Error = error;
    }

    public RecordingSession Session { get; }
    public string TemporaryWavPath { get; }
    public Exception Error { get; }
}
