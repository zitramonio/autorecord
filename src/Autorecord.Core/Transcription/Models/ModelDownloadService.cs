using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Autorecord.Core.Transcription.Models;

public sealed class ModelDownloadService(
    HttpClient httpClient,
    string downloadsRoot,
    Func<string, long?>? getAvailableFreeSpaceBytes = null)
{
    private const int BufferSize = 81920;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
            EnsureEnoughDiskSpace(root, totalBytes);
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

    public async Task<string> DownloadHuggingFaceSnapshotAsync(
        ModelCatalogEntry model,
        string? accessToken,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(model.Download.HuggingFaceRepoId))
        {
            throw new InvalidOperationException($"No Hugging Face repository is configured for model '{model.Id}'.");
        }

        if (model.Download.RequiresAuthorization && string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException($"Hugging Face access token is required for model '{model.Id}'.");
        }

        var root = Path.GetFullPath(downloadsRoot);
        Directory.CreateDirectory(root);
        var snapshotPath = CreateTempDirectoryPath(root, model.Id);

        try
        {
            Directory.CreateDirectory(snapshotPath);
            var repoId = model.Download.HuggingFaceRepoId.Trim();
            var revision = string.IsNullOrWhiteSpace(model.Download.HuggingFaceRevision)
                ? "main"
                : model.Download.HuggingFaceRevision.Trim();
            var files = await GetHuggingFaceFilesAsync(repoId, revision, accessToken, cancellationToken);
            if (files.Count == 0)
            {
                throw new InvalidOperationException($"Hugging Face repository '{repoId}' has no files to download.");
            }

            var totalBytes = files.Sum(file => file.Size ?? 0);
            EnsureEnoughDiskSpace(root, totalBytes > 0 ? totalBytes : null);
            var bytesDownloaded = 0L;
            var stopwatch = Stopwatch.StartNew();

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var destinationPath = GetContainedChildPath(snapshotPath, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                var url = BuildHuggingFaceResolveUrl(repoId, revision, file.Path);
                await DownloadHttpFileAsync(
                    url,
                    destinationPath,
                    accessToken,
                    file.Size,
                    downloaded =>
                    {
                        var reported = bytesDownloaded + downloaded;
                        progress?.Report(new ModelDownloadProgress
                        {
                            BytesDownloaded = reported,
                            TotalBytes = totalBytes > 0 ? totalBytes : null,
                            BytesPerSecond = CalculateBytesPerSecond(reported, stopwatch.Elapsed)
                        });
                    },
                    cancellationToken);
                bytesDownloaded += new FileInfo(destinationPath).Length;
            }

            progress?.Report(new ModelDownloadProgress
            {
                BytesDownloaded = bytesDownloaded,
                TotalBytes = totalBytes > 0 ? totalBytes : bytesDownloaded,
                BytesPerSecond = CalculateBytesPerSecond(bytesDownloaded, stopwatch.Elapsed)
            });
            return snapshotPath;
        }
        catch
        {
            DeleteTempDirectory(snapshotPath);
            throw;
        }
    }

    private async Task<IReadOnlyList<HuggingFaceTreeEntry>> GetHuggingFaceFilesAsync(
        string repoId,
        string revision,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        var url = $"https://huggingface.co/api/models/{repoId}/tree/{Uri.EscapeDataString(revision)}?recursive=1";
        using var response = await SendGetAsync(url, accessToken, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Hugging Face model listing failed with HTTP {(int)response.StatusCode} ({response.StatusCode}).");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var entries = await JsonSerializer.DeserializeAsync<IReadOnlyList<HuggingFaceTreeEntry>>(
            stream,
            JsonOptions,
            cancellationToken) ?? [];

        return entries
            .Where(entry => string.Equals(entry.Type, "file", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(entry.Path))
            .Select(entry => entry with { Path = entry.Path.Trim().Replace('\\', '/') })
            .ToArray();
    }

    private async Task DownloadHttpFileAsync(
        string url,
        string destinationPath,
        string? accessToken,
        long? expectedBytes,
        Action<long> reportFileBytesDownloaded,
        CancellationToken cancellationToken)
    {
        using var response = await SendGetAsync(url, accessToken, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Model download failed with HTTP {(int)response.StatusCode} ({response.StatusCode}).");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            useAsync: true);

        var buffer = new byte[BufferSize];
        var bytesDownloaded = 0L;
        while (true)
        {
            var bytesRead = await source.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            bytesDownloaded += bytesRead;
            reportFileBytesDownloaded(bytesDownloaded);
        }

        if (expectedBytes is > 0 && bytesDownloaded != expectedBytes.Value)
        {
            throw new InvalidOperationException(
                $"Downloaded file size mismatch. Expected {expectedBytes.Value} bytes, got {bytesDownloaded} bytes.");
        }
    }

    private async Task<HttpResponseMessage> SendGetAsync(
        string url,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Autorecord/1.0");
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());
        }

        return await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
    }

    private void EnsureEnoughDiskSpace(string root, long? requiredBytes)
    {
        if (requiredBytes is not > 0)
        {
            return;
        }

        var availableBytes = getAvailableFreeSpaceBytes?.Invoke(root) ?? GetAvailableFreeSpaceBytes(root);
        if (availableBytes is not null && availableBytes.Value < requiredBytes.Value)
        {
            throw new NotEnoughDiskSpaceException(requiredBytes.Value, availableBytes.Value);
        }
    }

    private static long? GetAvailableFreeSpaceBytes(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrWhiteSpace(root))
            {
                return null;
            }

            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
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

    private static string CreateTempDirectoryPath(string root, string modelId)
    {
        var directoryName = $"{SanitizeFileNamePart(modelId)}.{Guid.NewGuid():N}.snapshot";
        return GetContainedChildPath(root, directoryName);
    }

    private static string GetContainedChildPath(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException("Generated download path is outside downloads root.");
        }

        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        var normalizedRoot = Path.TrimEndingDirectorySeparator(fullRoot);
        if (!fullPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Generated download path is outside downloads root.");
        }

        return fullPath;
    }

    private static string BuildHuggingFaceResolveUrl(string repoId, string revision, string filePath)
    {
        return $"https://huggingface.co/{EscapePath(repoId)}/resolve/{Uri.EscapeDataString(revision)}/{EscapePath(filePath)}";
    }

    private static string EscapePath(string path)
    {
        return string.Join(
            "/",
            path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
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

    private static void DeleteTempDirectory(string tempPath)
    {
        try
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record HuggingFaceTreeEntry
    {
        public string Path { get; init; } = "";
        public string Type { get; init; } = "";
        public long? Size { get; init; }
    }
}
