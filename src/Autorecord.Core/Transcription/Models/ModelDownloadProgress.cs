namespace Autorecord.Core.Transcription.Models;

public sealed record ModelDownloadProgress
{
    public long BytesDownloaded { get; init; }
    public long? TotalBytes { get; init; }
    public double? BytesPerSecond { get; init; }

    public int Percent
    {
        get
        {
            if (TotalBytes is not > 0)
            {
                return 0;
            }

            var percent = BytesDownloaded * 100d / TotalBytes.Value;
            return (int)Math.Clamp(percent, 0d, 100d);
        }
    }
}
