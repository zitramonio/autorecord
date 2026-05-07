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
