namespace Autorecord.Core.Transcription.Models;

public sealed record InstalledModelManifest
{
    public IReadOnlyList<InstalledModelManifestEntry> Models { get; init; } = [];
}
