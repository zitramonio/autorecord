namespace Autorecord.Core.Settings;

public enum RecordingMode
{
    AllEvents = 0,
    TaggedEvents = 1
}

public enum TranscriptOutputFolderMode
{
    SameAsRecording = 0,
    CustomFolder = 1
}

public enum TranscriptOutputFormat
{
    Txt = 0,
    Markdown = 1,
    Srt = 2,
    Json = 3
}

public sealed record TranscriptionSettings
{
    public bool AutoTranscribeAfterRecording { get; init; }
    public string SelectedAsrModelId { get; init; } = "sherpa-gigaam-v2-ru-fast";
    public string SelectedDiarizationModelId { get; init; } = "sherpa-diarization-pyannote-fast";
    public TranscriptOutputFolderMode OutputFolderMode { get; init; } = TranscriptOutputFolderMode.SameAsRecording;
    public string? CustomOutputFolder { get; init; }
    public IReadOnlyList<TranscriptOutputFormat> OutputFormats { get; init; } =
    [
        TranscriptOutputFormat.Txt,
        TranscriptOutputFormat.Markdown,
        TranscriptOutputFormat.Srt,
        TranscriptOutputFormat.Json
    ];

    public bool EnableDiarization { get; init; }
    public int? NumSpeakers { get; init; }
    public double? ClusterThreshold { get; init; } = 0.65;
    public bool OverwriteExistingTranscripts { get; init; }
    public bool KeepIntermediateFiles { get; init; }

    public bool Equals(TranscriptionSettings? other)
    {
        return other is not null
            && AutoTranscribeAfterRecording == other.AutoTranscribeAfterRecording
            && SelectedAsrModelId == other.SelectedAsrModelId
            && SelectedDiarizationModelId == other.SelectedDiarizationModelId
            && OutputFolderMode == other.OutputFolderMode
            && CustomOutputFolder == other.CustomOutputFolder
            && OutputFormats.SequenceEqual(other.OutputFormats)
            && EnableDiarization == other.EnableDiarization
            && NumSpeakers == other.NumSpeakers
            && ClusterThreshold == other.ClusterThreshold
            && OverwriteExistingTranscripts == other.OverwriteExistingTranscripts
            && KeepIntermediateFiles == other.KeepIntermediateFiles;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(AutoTranscribeAfterRecording);
        hash.Add(SelectedAsrModelId);
        hash.Add(SelectedDiarizationModelId);
        hash.Add(OutputFolderMode);
        hash.Add(CustomOutputFolder);
        foreach (var format in OutputFormats)
        {
            hash.Add(format);
        }

        hash.Add(EnableDiarization);
        hash.Add(NumSpeakers);
        hash.Add(ClusterThreshold);
        hash.Add(OverwriteExistingTranscripts);
        hash.Add(KeepIntermediateFiles);
        return hash.ToHashCode();
    }
}

public sealed record AppSettings
{
    public string CalendarUrl { get; init; } = "";
    public string OutputFolder { get; init; } = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    public RecordingMode RecordingMode { get; init; } = RecordingMode.AllEvents;
    public string EventTag { get; init; } = "record";
    public int SilencePromptMinutes { get; init; } = 1;
    public int RetryPromptMinutes { get; init; } = 5;
    public bool KeepMicrophoneReady { get; init; } = true;
    public bool StartWithWindows { get; init; }
    public TranscriptionSettings Transcription { get; init; } = new();
}
