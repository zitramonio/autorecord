using Autorecord.Core.Transcription.Models;

namespace Autorecord.Core.Tests;

public sealed class ModelManagerTests
{
    [Fact]
    public async Task GetStatusAsyncReturnsInstalledWhenRequiredFilesExist()
    {
        var root = CreateTempRoot();
        var model = CreateModel(
            targetFolder: "asr-fast",
            requiredFiles: ["model.onnx", Path.Combine("tokens", "tokens.txt")]);
        var modelPath = Path.Combine(root, "asr-fast");
        Directory.CreateDirectory(Path.Combine(modelPath, "tokens"));
        await File.WriteAllTextAsync(Path.Combine(modelPath, "model.onnx"), "");
        await File.WriteAllTextAsync(Path.Combine(modelPath, "tokens", "tokens.txt"), "");
        var manager = new ModelManager(root);

        var status = await manager.GetStatusAsync(model, CancellationToken.None);

        Assert.Equal(ModelInstallStatus.Installed, status);
    }

    [Fact]
    public async Task GetStatusAsyncReturnsMissingRequiredFilesWhenFileIsAbsent()
    {
        var root = CreateTempRoot();
        var model = CreateModel(targetFolder: "asr-fast", requiredFiles: ["model.onnx", "tokens.txt"]);
        var modelPath = Path.Combine(root, "asr-fast");
        Directory.CreateDirectory(modelPath);
        await File.WriteAllTextAsync(Path.Combine(modelPath, "model.onnx"), "");
        var manager = new ModelManager(root);

        var status = await manager.GetStatusAsync(model, CancellationToken.None);

        Assert.Equal(ModelInstallStatus.MissingRequiredFiles, status);
    }

    [Fact]
    public async Task GetStatusAsyncReturnsNotInstalledWhenFolderMissing()
    {
        var root = CreateTempRoot();
        var model = CreateModel(targetFolder: "asr-fast", requiredFiles: ["model.onnx"]);
        var manager = new ModelManager(root);

        var status = await manager.GetStatusAsync(model, CancellationToken.None);

        Assert.Equal(ModelInstallStatus.NotInstalled, status);
    }

    [Fact]
    public async Task GetStatusAsyncReturnsDownloadUnavailableForPlaceholderModel()
    {
        var root = CreateTempRoot();
        var model = CreateModel(
            targetFolder: "gigaam",
            requiredFiles: [],
            download: new ModelDownloadInfo
            {
                Url = " ",
                SegmentationUrl = null,
                EmbeddingUrl = ""
            });
        var manager = new ModelManager(root);

        var status = await manager.GetStatusAsync(model, CancellationToken.None);

        Assert.Equal(ModelInstallStatus.DownloadUnavailable, status);
    }

    [Fact]
    public async Task DeleteAsyncRemovesInstalledModelFolder()
    {
        var root = CreateTempRoot();
        var model = CreateModel(targetFolder: "asr-fast", requiredFiles: ["model.onnx"]);
        var modelPath = Path.Combine(root, "asr-fast");
        Directory.CreateDirectory(modelPath);
        await File.WriteAllTextAsync(Path.Combine(modelPath, "model.onnx"), "");
        var manager = new ModelManager(root);

        await manager.DeleteAsync(model, CancellationToken.None);

        Assert.False(Directory.Exists(modelPath));
    }

    [Fact]
    public void GetModelPathRejectsParentTraversalTargetFolder()
    {
        var root = CreateTempRoot();
        var model = CreateModel(targetFolder: Path.Combine("..", "outside"), requiredFiles: ["model.onnx"]);
        var manager = new ModelManager(root);

        Assert.Throws<ArgumentException>(() => manager.GetModelPath(model));
    }

    [Fact]
    public void GetModelPathRejectsRootedTargetFolder()
    {
        var root = CreateTempRoot();
        var model = CreateModel(targetFolder: Path.Combine(root, "outside"), requiredFiles: ["model.onnx"]);
        var manager = new ModelManager(root);

        Assert.Throws<ArgumentException>(() => manager.GetModelPath(model));
    }

    [Fact]
    public void GetModelPathRejectsCurrentDirectoryTargetFolder()
    {
        var root = CreateTempRoot();
        var model = CreateModel(targetFolder: ".", requiredFiles: ["model.onnx"]);
        var manager = new ModelManager(root);

        Assert.Throws<ArgumentException>(() => manager.GetModelPath(model));
    }

    [Fact]
    public void GetModelPathRejectsTargetFolderThatNormalizesToModelsRoot()
    {
        var root = CreateTempRoot();
        var model = CreateModel(targetFolder: Path.Combine("asr-fast", ".."), requiredFiles: ["model.onnx"]);
        var manager = new ModelManager(root);

        Assert.Throws<ArgumentException>(() => manager.GetModelPath(model));
    }

    [Fact]
    public async Task GetStatusAsyncRejectsParentTraversalRequiredFile()
    {
        var root = CreateTempRoot();
        var model = CreateModel(
            targetFolder: "asr-fast",
            requiredFiles: [Path.Combine("..", "outside.txt")]);
        Directory.CreateDirectory(Path.Combine(root, "asr-fast"));
        var manager = new ModelManager(root);

        await Assert.ThrowsAsync<ArgumentException>(() => manager.GetStatusAsync(model, CancellationToken.None));
    }

    [Fact]
    public async Task GetStatusAsyncRejectsRootedRequiredFile()
    {
        var root = CreateTempRoot();
        var model = CreateModel(
            targetFolder: "asr-fast",
            requiredFiles: [Path.Combine(root, "outside.txt")]);
        Directory.CreateDirectory(Path.Combine(root, "asr-fast"));
        var manager = new ModelManager(root);

        await Assert.ThrowsAsync<ArgumentException>(() => manager.GetStatusAsync(model, CancellationToken.None));
    }

    [Fact]
    public async Task GetStatusAsyncRejectsRequiredFileThatNormalizesToModelFolder()
    {
        var root = CreateTempRoot();
        var model = CreateModel(targetFolder: "asr-fast", requiredFiles: ["."]);
        Directory.CreateDirectory(Path.Combine(root, "asr-fast"));
        var manager = new ModelManager(root);

        await Assert.ThrowsAsync<ArgumentException>(() => manager.GetStatusAsync(model, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsyncDoesNotDeleteOutsideModelsRoot()
    {
        var parent = CreateTempRoot();
        var root = Path.Combine(parent, "models");
        var outside = Path.Combine(parent, "outside");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        var model = CreateModel(targetFolder: Path.Combine("..", "outside"), requiredFiles: ["model.onnx"]);
        var manager = new ModelManager(root);

        await Assert.ThrowsAsync<ArgumentException>(() => manager.DeleteAsync(model, CancellationToken.None));

        Assert.True(Directory.Exists(outside));
    }

    [Fact]
    public async Task DeleteAsyncDoesNotDeleteModelsRootForCurrentDirectoryTargetFolder()
    {
        var root = CreateTempRoot();
        var model = CreateModel(targetFolder: ".", requiredFiles: ["model.onnx"]);
        var manager = new ModelManager(root);

        await Assert.ThrowsAsync<ArgumentException>(() => manager.DeleteAsync(model, CancellationToken.None));

        Assert.True(Directory.Exists(root));
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static ModelCatalogEntry CreateModel(
        string targetFolder,
        IReadOnlyList<string> requiredFiles,
        ModelDownloadInfo? download = null)
    {
        return new ModelCatalogEntry
        {
            Id = targetFolder,
            DisplayName = targetFolder,
            Type = "asr",
            Engine = "sherpa-onnx",
            Download = download ?? new ModelDownloadInfo { Url = "https://example.com/model.tar.bz2" },
            Install = new ModelInstallInfo
            {
                TargetFolder = targetFolder,
                RequiredFiles = requiredFiles
            }
        };
    }
}
