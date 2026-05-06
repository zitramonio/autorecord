namespace Autorecord.Core.Settings;

public enum RecordingMode
{
    AllEvents = 0,
    TaggedEvents = 1
}

public sealed record AppSettings
{
    public string CalendarUrl { get; init; } = "";
    public string OutputFolder { get; init; } = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    public RecordingMode RecordingMode { get; init; } = RecordingMode.AllEvents;
    public string EventTag { get; init; } = "record";
    public int SilencePromptMinutes { get; init; } = 1;
    public int RetryPromptMinutes { get; init; } = 5;
    public bool StartWithWindows { get; init; }
}
