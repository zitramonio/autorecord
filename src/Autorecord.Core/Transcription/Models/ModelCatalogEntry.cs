namespace Autorecord.Core.Transcription.Models;

public sealed record ModelCatalogEntry
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Description { get; init; } = "";
    public string Type { get; init; } = "";
    public string Engine { get; init; } = "";
    public string Language { get; init; } = "";
    public string Version { get; init; } = "";
    public int? SizeMb { get; init; }
    public bool RequiresDiarization { get; init; }
    public ModelDownloadInfo Download { get; init; } = new();
    public ModelInstallInfo Install { get; init; } = new();
    public ModelRuntimeInfo Runtime { get; init; } = new();
}
