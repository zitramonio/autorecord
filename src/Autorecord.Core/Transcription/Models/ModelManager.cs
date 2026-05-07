namespace Autorecord.Core.Transcription.Models;

public sealed class ModelManager
{
    public ModelManager(string modelsRoot)
    {
        ModelsRoot = modelsRoot;
    }

    public string ModelsRoot { get; }

    public string GetModelPath(ModelCatalogEntry model)
    {
        return Path.Combine(ModelsRoot, model.Install.TargetFolder);
    }

    public Task<ModelInstallStatus> GetStatusAsync(ModelCatalogEntry model, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (HasNoDownloadUrls(model.Download) && model.Install.RequiredFiles.Count == 0)
        {
            return Task.FromResult(ModelInstallStatus.DownloadUnavailable);
        }

        var modelPath = GetModelPath(model);
        if (!Directory.Exists(modelPath))
        {
            return Task.FromResult(ModelInstallStatus.NotInstalled);
        }

        foreach (var requiredFile in model.Install.RequiredFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(Path.Combine(modelPath, requiredFile)))
            {
                return Task.FromResult(ModelInstallStatus.MissingRequiredFiles);
            }
        }

        return Task.FromResult(ModelInstallStatus.Installed);
    }

    public Task DeleteAsync(ModelCatalogEntry model, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var modelPath = GetModelPath(model);
        if (Directory.Exists(modelPath))
        {
            Directory.Delete(modelPath, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static bool HasNoDownloadUrls(ModelDownloadInfo download)
    {
        return string.IsNullOrWhiteSpace(download.Url)
            && string.IsNullOrWhiteSpace(download.SegmentationUrl)
            && string.IsNullOrWhiteSpace(download.EmbeddingUrl);
    }
}
