namespace Autorecord.App.Transcription;

public sealed class TranscriptionJobActionRequestedEventArgs(Guid jobId) : EventArgs
{
    public Guid JobId { get; } = jobId;
}
