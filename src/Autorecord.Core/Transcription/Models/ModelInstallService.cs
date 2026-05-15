using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using SharpCompress.Readers;

namespace Autorecord.Core.Transcription.Models;

public sealed class ModelInstallService(ModelManager modelManager)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<InstalledModelManifestEntry> InstallAsync(
        ModelCatalogEntry model,
        IReadOnlyList<ModelInstallArtifact> artifacts,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (artifacts.Count == 0)
        {
            throw new InvalidOperationException($"No install artifacts were provided for model '{model.Id}'.");
        }

        var modelsRoot = Path.GetFullPath(modelManager.ModelsRoot);
        Directory.CreateDirectory(modelsRoot);

        var targetPath = modelManager.GetModelPath(model);
        var stagingPath = CreateStagingPath(modelsRoot, model.Install.TargetFolder);
        Directory.CreateDirectory(stagingPath);

        try
        {
            for (var i = 0; i < artifacts.Count; i++)
            {
                var artifact = artifacts[i];
                var sha256 = string.IsNullOrWhiteSpace(artifact.Sha256)
                    ? i == 0 ? model.Download.Sha256 : null
                    : artifact.Sha256;
                if (!artifact.IsDirectory)
                {
                    await VerifySha256Async(artifact.Path, sha256, cancellationToken);
                }

                await InstallArtifactAsync(artifact, stagingPath, cancellationToken);
            }

            FlattenSingleTopLevelFolderIfNeeded(model, stagingPath);
            ValidateRequiredFiles(model, stagingPath, cancellationToken);
            var publish = PublishStagingFolder(stagingPath, targetPath);
            var entry = CreateManifestEntry(model, targetPath);
            try
            {
                await UpdateManifestAsync(modelsRoot, entry, cancellationToken);
                publish.Commit();
            }
            catch
            {
                publish.Rollback();
                throw;
            }

            return entry;
        }
        catch
        {
            DeleteDirectoryQuietly(stagingPath);
            throw;
        }
    }

    private static async Task InstallArtifactAsync(
        ModelInstallArtifact artifact,
        string stagingPath,
        CancellationToken cancellationToken)
    {
        if (artifact.IsDirectory)
        {
            await CopyDirectoryAsync(artifact.Path, stagingPath, cancellationToken);
            return;
        }

        var archiveType = artifact.ArchiveType?.Trim();
        if (string.IsNullOrWhiteSpace(archiveType))
        {
            await CopyPlainFileAsync(artifact, stagingPath, cancellationToken);
            return;
        }

        if (string.Equals(archiveType, "zip", StringComparison.OrdinalIgnoreCase))
        {
            await ExtractZipAsync(artifact.Path, stagingPath, cancellationToken);
            return;
        }

        if (string.Equals(archiveType, "tar.bz2", StringComparison.OrdinalIgnoreCase))
        {
            await ExtractTarBz2Async(artifact.Path, stagingPath, cancellationToken);
            return;
        }

        throw new InvalidOperationException($"Unsupported model archive type '{archiveType}'.");
    }

    private static async Task CopyDirectoryAsync(
        string sourcePath,
        string stagingPath,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(sourcePath))
        {
            throw new DirectoryNotFoundException($"Model artifact directory does not exist: {sourcePath}");
        }

        foreach (var filePath in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(sourcePath, filePath);
            var destinationPath = GetContainedChildPath(stagingPath, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destinationPath)!);
            await using var source = File.OpenRead(filePath);
            await using var destination = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);
            await source.CopyToAsync(destination, cancellationToken);
        }
    }

    private static async Task CopyPlainFileAsync(
        ModelInstallArtifact artifact,
        string stagingPath,
        CancellationToken cancellationToken)
    {
        var fileName = string.IsNullOrWhiteSpace(artifact.TargetFileName)
            ? System.IO.Path.GetFileName(artifact.Path)
            : artifact.TargetFileName;
        var destinationPath = GetContainedChildPath(stagingPath, fileName);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destinationPath)!);

        await using var source = File.OpenRead(artifact.Path);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);
        await source.CopyToAsync(destination, cancellationToken);
    }

    private static async Task ExtractZipAsync(
        string archivePath,
        string stagingPath,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            var destinationPath = GetContainedChildPath(stagingPath, entry.FullName);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destinationPath)!);
            await using var source = entry.Open();
            await using var destination = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);
            await source.CopyToAsync(destination, cancellationToken);
        }
    }

    private static async Task ExtractTarBz2Async(
        string archivePath,
        string stagingPath,
        CancellationToken cancellationToken)
    {
        await using var file = File.OpenRead(archivePath);
        using var reader = ReaderFactory.OpenReader(file, new ReaderOptions());
        while (reader.MoveToNextEntry())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.Entry.IsDirectory)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(reader.Entry.Key))
            {
                continue;
            }

            var destinationPath = GetContainedChildPath(stagingPath, reader.Entry.Key);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destinationPath)!);
            await using var source = reader.OpenEntryStream();
            await using var destination = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);
            await source.CopyToAsync(destination, cancellationToken);
        }
    }

    private static void ValidateRequiredFiles(
        ModelCatalogEntry model,
        string stagingPath,
        CancellationToken cancellationToken)
    {
        foreach (var requiredFile in model.Install.RequiredFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var requiredFilePath = GetContainedChildPath(stagingPath, requiredFile);
            if (!File.Exists(requiredFilePath))
            {
                throw new InvalidOperationException(
                    $"Model '{model.Id}' is missing required file '{requiredFile}'.");
            }
        }
    }

    private static void FlattenSingleTopLevelFolderIfNeeded(ModelCatalogEntry model, string stagingPath)
    {
        if (model.Install.RequiredFiles.Count == 0 || HasRequiredFiles(model, stagingPath))
        {
            return;
        }

        var directories = Directory.EnumerateDirectories(stagingPath).ToArray();
        if (directories.Length != 1 || !HasRequiredFilesAcrossRoots(model, stagingPath, directories[0]))
        {
            return;
        }

        var childFiles = Directory.EnumerateFiles(directories[0]).ToArray();
        var childDirectories = Directory.EnumerateDirectories(directories[0]).ToArray();
        if (childFiles.Any(filePath => File.Exists(System.IO.Path.Combine(stagingPath, System.IO.Path.GetFileName(filePath)))) ||
            childDirectories.Any(directoryPath => Directory.Exists(System.IO.Path.Combine(stagingPath, System.IO.Path.GetFileName(directoryPath)))))
        {
            return;
        }

        foreach (var filePath in childFiles)
        {
            var destinationPath = System.IO.Path.Combine(stagingPath, System.IO.Path.GetFileName(filePath));
            File.Move(filePath, destinationPath);
        }

        foreach (var directoryPath in childDirectories)
        {
            var destinationPath = System.IO.Path.Combine(stagingPath, System.IO.Path.GetFileName(directoryPath));
            Directory.Move(directoryPath, destinationPath);
        }

        Directory.Delete(directories[0]);
    }

    private static bool HasRequiredFiles(ModelCatalogEntry model, string root)
    {
        foreach (var requiredFile in model.Install.RequiredFiles)
        {
            if (!File.Exists(GetContainedChildPath(root, requiredFile)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasRequiredFilesAcrossRoots(ModelCatalogEntry model, string root, string childRoot)
    {
        foreach (var requiredFile in model.Install.RequiredFiles)
        {
            if (!File.Exists(GetContainedChildPath(root, requiredFile)) &&
                !File.Exists(GetContainedChildPath(childRoot, requiredFile)))
            {
                return false;
            }
        }

        return true;
    }

    private static PublishedModelFolder PublishStagingFolder(string stagingPath, string targetPath)
    {
        var backupPath = $"{targetPath}.backup.{Guid.NewGuid():N}";
        var hadExistingTarget = Directory.Exists(targetPath);

        if (hadExistingTarget)
        {
            Directory.Move(targetPath, backupPath);
        }

        try
        {
            Directory.Move(stagingPath, targetPath);
            return new PublishedModelFolder(targetPath, backupPath, hadExistingTarget);
        }
        catch
        {
            if (Directory.Exists(targetPath))
            {
                Directory.Delete(targetPath, recursive: true);
            }

            if (hadExistingTarget && Directory.Exists(backupPath))
            {
                Directory.Move(backupPath, targetPath);
            }

            throw;
        }
    }

    private sealed class PublishedModelFolder(string targetPath, string backupPath, bool hasBackup)
    {
        private bool _completed;

        public void Commit()
        {
            try
            {
                if (hasBackup && Directory.Exists(backupPath))
                {
                    Directory.Delete(backupPath, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            _completed = true;
        }

        public void Rollback()
        {
            if (_completed)
            {
                return;
            }

            if (Directory.Exists(targetPath))
            {
                Directory.Delete(targetPath, recursive: true);
            }

            if (hasBackup && Directory.Exists(backupPath))
            {
                Directory.Move(backupPath, targetPath);
            }

            _completed = true;
        }
    }

    private static InstalledModelManifestEntry CreateManifestEntry(ModelCatalogEntry model, string targetPath)
    {
        var files = Directory.EnumerateFiles(targetPath, "*", SearchOption.AllDirectories)
            .Select(path => System.IO.Path.GetRelativePath(targetPath, path))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var totalSizeBytes = Directory.EnumerateFiles(targetPath, "*", SearchOption.AllDirectories)
            .Sum(path => new FileInfo(path).Length);

        return new InstalledModelManifestEntry
        {
            Id = model.Id,
            DisplayName = model.DisplayName,
            Engine = model.Engine,
            Version = model.Version,
            LocalPath = targetPath,
            InstalledAt = DateTimeOffset.UtcNow,
            TotalSizeBytes = totalSizeBytes,
            Files = files,
            Status = ModelInstallStatus.Installed
        };
    }

    private static async Task UpdateManifestAsync(
        string modelsRoot,
        InstalledModelManifestEntry entry,
        CancellationToken cancellationToken)
    {
        var manifestPath = System.IO.Path.Combine(modelsRoot, "manifest.json");
        var manifest = new InstalledModelManifest();
        if (File.Exists(manifestPath))
        {
            await using var readStream = File.OpenRead(manifestPath);
            manifest = await JsonSerializer.DeserializeAsync<InstalledModelManifest>(
                readStream,
                JsonOptions,
                cancellationToken) ?? new InstalledModelManifest();
        }

        var entries = manifest.Models
            .Where(model => !string.Equals(model.Id, entry.Id, StringComparison.OrdinalIgnoreCase))
            .Append(entry)
            .OrderBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var updatedManifest = manifest with { Models = entries };
        var tempManifestPath = $"{manifestPath}.{Guid.NewGuid():N}.tmp";
        await using (var writeStream = new FileStream(
            tempManifestPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true))
        {
            await JsonSerializer.SerializeAsync(writeStream, updatedManifest, JsonOptions, cancellationToken);
        }

        File.Move(tempManifestPath, manifestPath, overwrite: true);
    }

    private static async Task VerifySha256Async(
        string path,
        string? expectedSha256,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256))
        {
            return;
        }

        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        var actualSha256 = Convert.ToHexString(hash).ToLowerInvariant();
        if (!string.Equals(actualSha256, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Model artifact sha256 mismatch. Expected {expectedSha256}, got {actualSha256}.");
        }
    }

    private static string CreateStagingPath(string modelsRoot, string targetFolder)
    {
        var safeName = SanitizeFileNamePart(targetFolder);
        return GetContainedChildPath(modelsRoot, $".install-{safeName}-{Guid.NewGuid():N}");
    }

    private static string GetContainedChildPath(string root, string relativePath)
    {
        if (System.IO.Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException("Model path must be relative and inside the models root.");
        }

        var fullRoot = System.IO.Path.GetFullPath(root);
        var fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(fullRoot, relativePath));
        var normalizedRoot = System.IO.Path.TrimEndingDirectorySeparator(fullRoot);

        if (!fullPath.StartsWith(normalizedRoot + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Model path is outside the models root.");
        }

        return fullPath;
    }

    private static string SanitizeFileNamePart(string value)
    {
        var invalidChars = System.IO.Path.GetInvalidFileNameChars();
        var chars = value.Select(c =>
            invalidChars.Contains(c) ||
            c == System.IO.Path.DirectorySeparatorChar ||
            c == System.IO.Path.AltDirectorySeparatorChar
                ? '_'
                : c).ToArray();
        var sanitized = new string(chars).Trim('.');
        return string.IsNullOrWhiteSpace(sanitized) ? "model" : sanitized;
    }

    private static void DeleteDirectoryQuietly(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
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
