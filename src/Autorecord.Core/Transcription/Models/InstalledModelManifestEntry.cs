namespace Autorecord.Core.Transcription.Models;

public sealed record InstalledModelManifestEntry
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Engine { get; init; } = "";
    public string Version { get; init; } = "";
    public string LocalPath { get; init; } = "";
    public DateTimeOffset InstalledAt { get; init; }
    public long TotalSizeBytes { get; init; }
    public IReadOnlyList<string> Files { get; init; } = [];
    public ModelInstallStatus Status { get; init; }
}
