namespace Autorecord.Core.Transcription.Models;

public sealed record ModelDownloadInfo
{
    public string? Url { get; init; }
    public string? SegmentationUrl { get; init; }
    public string? EmbeddingUrl { get; init; }
    public string? ArchiveType { get; init; }
    public string? Sha256 { get; init; }
}
