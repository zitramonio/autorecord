namespace Autorecord.Core.Transcription.Models;

public sealed class ModelManager
{
    public ModelManager(string modelsRoot)
    {
        ModelsRoot = Path.GetFullPath(modelsRoot);
    }

    public string ModelsRoot { get; }

    public string GetModelPath(ModelCatalogEntry model)
    {
        if (Path.IsPathRooted(model.Install.TargetFolder))
        {
            throw new ArgumentException("Model target folder must be relative.", nameof(model));
        }

        return GetContainedChildPath(ModelsRoot, model.Install.TargetFolder, nameof(model));
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

            if (Path.IsPathRooted(requiredFile))
            {
                throw new ArgumentException("Model required file must be relative.", nameof(model));
            }

            var requiredFilePath = GetContainedChildPath(modelPath, requiredFile, nameof(model));
            if (!File.Exists(requiredFilePath))
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

    private static string GetContainedChildPath(string root, string relativePath, string parameterName)
    {
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));

        if (!IsPathStrictlyInsideRoot(fullPath, fullRoot))
        {
            throw new ArgumentException("Model path must be inside the models root.", parameterName);
        }

        return fullPath;
    }

    private static bool IsPathStrictlyInsideRoot(string path, string root)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(root);

        return path.StartsWith(
            normalizedRoot + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }
}
