namespace Autorecord.Core.Transcription.Models;

public sealed record ModelInstallArtifact
{
    public string Path { get; init; } = "";
    public string? ArchiveType { get; init; }
    public string? TargetFileName { get; init; }
    public string? Sha256 { get; init; }
}
