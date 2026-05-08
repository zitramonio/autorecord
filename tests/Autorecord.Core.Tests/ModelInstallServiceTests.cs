using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Autorecord.Core.Transcription.Models;
using SharpCompress.Common;
using SharpCompress.Writers;

namespace Autorecord.Core.Tests;

public sealed class ModelInstallServiceTests
{
    [Fact]
    public async Task InstallAsyncInstallsZipModelAndWritesManifest()
    {
        var root = CreateTempRoot();
        var archivePath = Path.Combine(root, "model.zip");
        CreateZipArchive(archivePath, new Dictionary<string, string>
        {
            ["model.onnx"] = "onnx",
            ["tokens.txt"] = "tokens"
        });
        var model = CreateModel(
            id: "zip-model",
            targetFolder: "zip-model",
            archiveType: "zip",
            requiredFiles: ["model.onnx", "tokens.txt"]);
        var service = new ModelInstallService(new ModelManager(root));

        var entry = await service.InstallAsync(
            model,
            [new ModelInstallArtifact { Path = archivePath, ArchiveType = "zip" }],
            CancellationToken.None);

        var modelPath = Path.Combine(root, "zip-model");
        Assert.True(File.Exists(Path.Combine(modelPath, "model.onnx")));
        Assert.True(File.Exists(Path.Combine(modelPath, "tokens.txt")));
        Assert.Equal(ModelInstallStatus.Installed, entry.Status);
        Assert.Equal(modelPath, entry.LocalPath);
        Assert.Equal(["model.onnx", "tokens.txt"], entry.Files.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(10, entry.TotalSizeBytes);

        var manifest = await ReadManifestAsync(root);
        var manifestEntry = Assert.Single(manifest.Models);
        Assert.Equal("zip-model", manifestEntry.Id);
        Assert.Equal(ModelInstallStatus.Installed, manifestEntry.Status);
    }

    [Fact]
    public async Task InstallAsyncCopiesPlainFileAndWritesManifest()
    {
        var root = CreateTempRoot();
        var artifactPath = Path.Combine(root, "embedding.download");
        await File.WriteAllTextAsync(artifactPath, "embedding");
        var model = CreateModel(
            id: "plain-model",
            targetFolder: "plain-model",
            archiveType: null,
            requiredFiles: ["embedding.onnx"]);
        var service = new ModelInstallService(new ModelManager(root));

        await service.InstallAsync(
            model,
            [new ModelInstallArtifact { Path = artifactPath, TargetFileName = "embedding.onnx" }],
            CancellationToken.None);

        Assert.Equal("embedding", await File.ReadAllTextAsync(Path.Combine(root, "plain-model", "embedding.onnx")));
        var manifest = await ReadManifestAsync(root);
        Assert.Equal("plain-model", Assert.Single(manifest.Models).Id);
    }

    [Fact]
    public async Task InstallAsyncRejectsMissingRequiredFiles()
    {
        var root = CreateTempRoot();
        var archivePath = Path.Combine(root, "model.zip");
        CreateZipArchive(archivePath, new Dictionary<string, string>
        {
            ["model.onnx"] = "onnx"
        });
        var model = CreateModel(
            id: "missing-model",
            targetFolder: "missing-model",
            archiveType: "zip",
            requiredFiles: ["model.onnx", "tokens.txt"]);
        var service = new ModelInstallService(new ModelManager(root));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.InstallAsync(
                model,
                [new ModelInstallArtifact { Path = archivePath, ArchiveType = "zip" }],
                CancellationToken.None));

        Assert.Contains("required", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(root, "missing-model")));
        Assert.False(File.Exists(Path.Combine(root, "manifest.json")));
    }

    [Fact]
    public async Task InstallAsyncRejectsChecksumMismatch()
    {
        var root = CreateTempRoot();
        var archivePath = Path.Combine(root, "model.zip");
        CreateZipArchive(archivePath, new Dictionary<string, string>
        {
            ["model.onnx"] = "onnx"
        });
        var model = CreateModel(
            id: "checksum-model",
            targetFolder: "checksum-model",
            archiveType: "zip",
            requiredFiles: ["model.onnx"],
            sha256: new string('0', 64));
        var service = new ModelInstallService(new ModelManager(root));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.InstallAsync(
                model,
                [new ModelInstallArtifact { Path = archivePath, ArchiveType = "zip" }],
                CancellationToken.None));

        Assert.Contains("sha256", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(root, "checksum-model")));
    }

    [Fact]
    public async Task InstallAsyncRejectsArchivePathTraversal()
    {
        var root = CreateTempRoot();
        var archivePath = Path.Combine(root, "traversal.zip");
        CreateZipArchive(archivePath, new Dictionary<string, string>
        {
            ["../outside.txt"] = "outside",
            ["model.onnx"] = "onnx"
        });
        var model = CreateModel(
            id: "traversal-model",
            targetFolder: "traversal-model",
            archiveType: "zip",
            requiredFiles: ["model.onnx"]);
        var service = new ModelInstallService(new ModelManager(root));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.InstallAsync(
                model,
                [new ModelInstallArtifact { Path = archivePath, ArchiveType = "zip" }],
                CancellationToken.None));

        Assert.Contains("outside", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(root, "outside.txt")));
        Assert.False(Directory.Exists(Path.Combine(root, "traversal-model")));
    }

    [Fact]
    public async Task InstallAsyncExtractsTarBz2Archive()
    {
        var root = CreateTempRoot();
        var archivePath = Path.Combine(root, "model.tar.bz2");
        CreateTarBz2Archive(archivePath, new Dictionary<string, string>
        {
            ["model.int8.onnx"] = "onnx",
            ["tokens.txt"] = "tokens"
        });
        var model = CreateModel(
            id: "tar-model",
            targetFolder: "tar-model",
            archiveType: "tar.bz2",
            requiredFiles: ["model.int8.onnx", "tokens.txt"]);
        var service = new ModelInstallService(new ModelManager(root));

        await service.InstallAsync(
            model,
            [new ModelInstallArtifact { Path = archivePath, ArchiveType = "tar.bz2" }],
            CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(root, "tar-model", "model.int8.onnx")));
        Assert.True(File.Exists(Path.Combine(root, "tar-model", "tokens.txt")));
    }

    [Fact]
    public async Task InstallAsyncFlattensSingleTopLevelArchiveFolder()
    {
        var root = CreateTempRoot();
        var archivePath = Path.Combine(root, "foldered-model.tar.bz2");
        CreateTarBz2Archive(archivePath, new Dictionary<string, string>
        {
            ["upstream-folder/model.int8.onnx"] = "onnx",
            ["upstream-folder/tokens.txt"] = "tokens"
        });
        var model = CreateModel(
            id: "foldered-model",
            targetFolder: "foldered-model",
            archiveType: "tar.bz2",
            requiredFiles: ["model.int8.onnx", "tokens.txt"]);
        var service = new ModelInstallService(new ModelManager(root));

        await service.InstallAsync(
            model,
            [new ModelInstallArtifact { Path = archivePath, ArchiveType = "tar.bz2" }],
            CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(root, "foldered-model", "model.int8.onnx")));
        Assert.True(File.Exists(Path.Combine(root, "foldered-model", "tokens.txt")));
        Assert.False(Directory.Exists(Path.Combine(root, "foldered-model", "upstream-folder")));
    }

    [Fact]
    public async Task InstallAsyncRestoresExistingModelAndManifestWhenManifestUpdateFailsAfterPublish()
    {
        var root = CreateTempRoot();
        var model = CreateModel(
            id: "replace-model",
            targetFolder: "replace-model",
            archiveType: "zip",
            requiredFiles: ["model.onnx"]);
        var modelPath = Path.Combine(root, "replace-model");
        Directory.CreateDirectory(modelPath);
        await File.WriteAllTextAsync(Path.Combine(modelPath, "model.onnx"), "old");
        await WriteManifestAsync(
            root,
            new InstalledModelManifest
            {
                Models =
                [
                    new InstalledModelManifestEntry
                    {
                        Id = "replace-model",
                        DisplayName = "Old Model",
                        Engine = "old-engine",
                        Version = "old-version",
                        LocalPath = modelPath,
                        InstalledAt = DateTimeOffset.Parse("2026-05-01T00:00:00Z"),
                        TotalSizeBytes = 3,
                        Files = ["model.onnx"],
                        Status = ModelInstallStatus.Installed
                    }
                ]
            });
        var oldManifestJson = await File.ReadAllTextAsync(Path.Combine(root, "manifest.json"));
        var archivePath = Path.Combine(root, "replacement.zip");
        CreateZipArchive(archivePath, new Dictionary<string, string>
        {
            ["model.onnx"] = "new"
        });
        var service = new ModelInstallService(new ModelManager(root));

        await using (File.Open(Path.Combine(root, "manifest.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await Assert.ThrowsAsync<IOException>(
                () => service.InstallAsync(
                    model,
                    [new ModelInstallArtifact { Path = archivePath, ArchiveType = "zip" }],
                    CancellationToken.None));
        }

        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(modelPath, "model.onnx")));
        Assert.Equal(oldManifestJson, await File.ReadAllTextAsync(Path.Combine(root, "manifest.json")));
        Assert.Empty(Directory.EnumerateDirectories(root, "*.backup.*"));
    }

    private static void CreateZipArchive(string path, IReadOnlyDictionary<string, string> entries)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = archive.CreateEntry(name);
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(content);
        }
    }

    private static void CreateTarBz2Archive(string path, IReadOnlyDictionary<string, string> entries)
    {
        using var stream = File.Create(path);
        using var writer = WriterFactory.Open(
            stream,
            ArchiveType.Tar,
            new WriterOptions(CompressionType.BZip2));
        foreach (var (name, content) in entries)
        {
            var source = Path.Combine(Path.GetDirectoryName(path)!, $"{Guid.NewGuid():N}.tmp");
            File.WriteAllText(source, content);
            writer.Write(name, source);
            File.Delete(source);
        }
    }

    private static async Task<InstalledModelManifest> ReadManifestAsync(string root)
    {
        await using var stream = File.OpenRead(Path.Combine(root, "manifest.json"));
        return await JsonSerializer.DeserializeAsync<InstalledModelManifest>(
            stream,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new InstalledModelManifest();
    }

    private static async Task WriteManifestAsync(string root, InstalledModelManifest manifest)
    {
        await using var stream = File.Create(Path.Combine(root, "manifest.json"));
        await JsonSerializer.SerializeAsync(
            stream,
            manifest,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static ModelCatalogEntry CreateModel(
        string id,
        string targetFolder,
        string? archiveType,
        IReadOnlyList<string> requiredFiles,
        string? sha256 = null)
    {
        return new ModelCatalogEntry
        {
            Id = id,
            DisplayName = id,
            Type = "asr",
            Engine = "sherpa-onnx",
            Version = "1",
            Download = new ModelDownloadInfo
            {
                Url = "https://example.com/model",
                ArchiveType = archiveType,
                Sha256 = sha256
            },
            Install = new ModelInstallInfo
            {
                TargetFolder = targetFolder,
                RequiredFiles = requiredFiles
            }
        };
    }
}
