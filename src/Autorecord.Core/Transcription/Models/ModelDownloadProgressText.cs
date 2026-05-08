using System.Globalization;

namespace Autorecord.Core.Transcription.Models;

public static class ModelDownloadProgressText
{
    public static string Format(ModelDownloadProgress progress)
    {
        var downloaded = FormatBytes(progress.BytesDownloaded);
        var speed = progress.BytesPerSecond is >= 0
            ? $"{FormatBytes((long)progress.BytesPerSecond.Value)}/с"
            : "—";

        if (progress.TotalBytes is > 0)
        {
            return $"Скачано {downloaded} из {FormatBytes(progress.TotalBytes.Value)}, скорость {speed}";
        }

        return $"Скачано {downloaded}, скорость {speed}";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        var kilobytes = bytes / 1024d;
        if (kilobytes < 1024)
        {
            return $"{kilobytes.ToString("0.0", CultureInfo.InvariantCulture)} KB";
        }

        var megabytes = kilobytes / 1024d;
        if (megabytes < 1024)
        {
            return $"{megabytes.ToString("0.0", CultureInfo.InvariantCulture)} MB";
        }

        return $"{(megabytes / 1024d).ToString("0.0", CultureInfo.InvariantCulture)} GB";
    }
}
