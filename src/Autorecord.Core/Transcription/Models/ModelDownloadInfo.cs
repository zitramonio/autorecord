namespace Autorecord.Core.Transcription.Models;

public sealed record ModelDownloadInfo
{
    public string? Url { get; init; }
    public string? SegmentationUrl { get; init; }
    public string? EmbeddingUrl { get; init; }
    public string? HuggingFaceRepoId { get; init; }
    public string? HuggingFaceRevision { get; init; }
    public bool RequiresAuthorization { get; init; }
    public string? ArchiveType { get; init; }
    public string? Sha256 { get; init; }
}
