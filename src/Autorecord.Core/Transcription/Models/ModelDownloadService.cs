using System.Diagnostics;

namespace Autorecord.Core.Transcription.Models;

public sealed class ModelDownloadService(HttpClient httpClient, string downloadsRoot)
{
    private const int BufferSize = 81920;

    public async Task<string> DownloadAsync(
        ModelCatalogEntry model,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var url = SelectDownloadUrl(model);
        return await DownloadFileAsync(url, model.Id, progress, cancellationToken);
    }

    public async Task<string> DownloadFileAsync(
        string url,
        string fileNameHint,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException("No download URL is available.");
        }

        var root = Path.GetFullPath(downloadsRoot);
        Directory.CreateDirectory(root);

        var tempPath = CreateTempPath(root, fileNameHint);

        try
        {
            using var response = await httpClient.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Model download failed with HTTP {(int)response.StatusCode} ({response.StatusCode}).");
            }

            var totalBytes = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destination = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                useAsync: true);

            var buffer = new byte[BufferSize];
            var bytesDownloaded = 0L;
            var stopwatch = Stopwatch.StartNew();

            while (true)
            {
                var bytesRead = await source.ReadAsync(buffer, cancellationToken);
                if (bytesRead == 0)
                {
                    break;
                }

                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                bytesDownloaded += bytesRead;
                progress?.Report(new ModelDownloadProgress
                {
                    BytesDownloaded = bytesDownloaded,
                    TotalBytes = totalBytes,
                    BytesPerSecond = CalculateBytesPerSecond(bytesDownloaded, stopwatch.Elapsed)
                });
            }

            return tempPath;
        }
        catch
        {
            DeleteTempFile(tempPath);
            throw;
        }
    }

    private static string SelectDownloadUrl(ModelCatalogEntry model)
    {
        if (!string.IsNullOrWhiteSpace(model.Download.Url))
        {
            return model.Download.Url;
        }

        if (!string.IsNullOrWhiteSpace(model.Download.SegmentationUrl))
        {
            return model.Download.SegmentationUrl;
        }

        throw new InvalidOperationException($"No download URL is available for model '{model.Id}'.");
    }

    private static string CreateTempPath(string root, string modelId)
    {
        var fileName = $"{SanitizeFileNamePart(modelId)}.{Guid.NewGuid():N}.download";
        var path = Path.GetFullPath(Path.Combine(root, fileName));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!path.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Generated download path is outside downloads root.");
        }

        return path;
    }

    private static string SanitizeFileNamePart(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var chars = value.Select(c =>
            invalidChars.Contains(c) ||
            c == Path.DirectorySeparatorChar ||
            c == Path.AltDirectorySeparatorChar
                ? '_'
                : c).ToArray();
        var sanitized = new string(chars).Trim('.');
        return string.IsNullOrWhiteSpace(sanitized) ? "model" : sanitized;
    }

    private static double CalculateBytesPerSecond(long bytesDownloaded, TimeSpan elapsed)
    {
        return elapsed.TotalSeconds > 0
            ? bytesDownloaded / elapsed.TotalSeconds
            : 0;
    }

    private static void DeleteTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
