namespace Autorecord.Core.Transcription.Jobs;

public enum TranscriptionJobStatus
{
    Pending = 0,
    WaitingForModel = 1,
    Running = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5
}
