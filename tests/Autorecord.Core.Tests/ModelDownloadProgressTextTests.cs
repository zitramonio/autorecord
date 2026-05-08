using Autorecord.Core.Transcription.Models;

namespace Autorecord.Core.Tests;

public sealed class ModelDownloadProgressTextTests
{
    [Fact]
    public void FormatIncludesDownloadedTotalAndSpeed()
    {
        var progress = new ModelDownloadProgress
        {
            BytesDownloaded = 1_572_864,
            TotalBytes = 3_145_728,
            BytesPerSecond = 524_288
        };

        var text = ModelDownloadProgressText.Format(progress);

        Assert.Equal("Скачано 1.5 MB из 3.0 MB, скорость 512.0 KB/с", text);
    }

    [Fact]
    public void FormatHandlesUnknownTotalAndSpeed()
    {
        var progress = new ModelDownloadProgress
        {
            BytesDownloaded = 512,
            TotalBytes = null,
            BytesPerSecond = null
        };

        var text = ModelDownloadProgressText.Format(progress);

        Assert.Equal("Скачано 512 B, скорость —", text);
    }
}
