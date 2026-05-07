using Autorecord.Core.Settings;

namespace Autorecord.App.Transcription;

public sealed record TranscriptionSettingsViewModel
{
    public bool AutoTranscribeAfterRecording { get; init; }
    public string SelectedAsrModelId { get; init; } = "";
    public string SelectedDiarizationModelId { get; init; } = "";
    public bool EnableDiarization { get; init; }
    public int? NumSpeakers { get; init; }
    public TranscriptOutputFolderMode OutputFolderMode { get; init; }
    public string? CustomOutputFolder { get; init; }
    public IReadOnlyList<TranscriptOutputFormat> OutputFormats { get; init; } = [];
}
