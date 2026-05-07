using System.Text.Json;

namespace Autorecord.Core.Transcription.Models;

public sealed class ModelCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private ModelCatalog(IReadOnlyList<ModelCatalogEntry> models)
    {
        Models = models;
    }

    public IReadOnlyList<ModelCatalogEntry> Models { get; }

    public static async Task<ModelCatalog> LoadAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var document = await JsonSerializer.DeserializeAsync<CatalogDocument>(
            stream,
            JsonOptions,
            cancellationToken);

        if (document?.Models is null)
        {
            throw new InvalidOperationException("Model catalog must contain a non-null models array.");
        }

        var models = document.Models;
        Validate(models);
        return new ModelCatalog(models.Select(model => model!).ToArray());
    }

    public IEnumerable<ModelCatalogEntry> GetByType(string type)
    {
        return Models.Where(model => string.Equals(model.Type, type, StringComparison.OrdinalIgnoreCase));
    }

    public ModelCatalogEntry GetRequired(string id)
    {
        return Models.FirstOrDefault(model => string.Equals(model.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Model '{id}' was not found in catalog.");
    }

    private static void Validate(IReadOnlyList<ModelCatalogEntry?> models)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in models)
        {
            if (model is null)
            {
                throw new InvalidOperationException("Model catalog must not contain null models.");
            }

            RequireKeyString(model.Id, nameof(ModelCatalogEntry.Id));
            RequireKeyString(model.DisplayName, nameof(ModelCatalogEntry.DisplayName));
            RequireKeyString(model.Type, nameof(ModelCatalogEntry.Type));
            RequireKeyString(model.Engine, nameof(ModelCatalogEntry.Engine));
            if (model.Download is null)
            {
                throw new InvalidOperationException($"Model catalog field '{nameof(ModelCatalogEntry.Download)}' must not be null.");
            }

            if (model.Install is null)
            {
                throw new InvalidOperationException($"Model catalog field '{nameof(ModelCatalogEntry.Install)}' must not be null.");
            }

            if (model.Runtime is null)
            {
                throw new InvalidOperationException($"Model catalog field '{nameof(ModelCatalogEntry.Runtime)}' must not be null.");
            }

            RequireKeyString(model.Install.TargetFolder, nameof(ModelInstallInfo.TargetFolder));
            ValidateRequiredFiles(model.Install.RequiredFiles);

            if (!ids.Add(model.Id))
            {
                throw new InvalidOperationException($"Duplicate model id '{model.Id}' in catalog.");
            }
        }
    }

    private static void RequireKeyString(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Model catalog field '{name}' must not be empty.");
        }

        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Model catalog field '{name}' must not have leading or trailing whitespace.");
        }
    }

    private static void ValidateRequiredFiles(IReadOnlyList<string>? requiredFiles)
    {
        if (requiredFiles is null)
        {
            throw new InvalidOperationException($"Model catalog field '{nameof(ModelInstallInfo.RequiredFiles)}' must not be null.");
        }

        foreach (var requiredFile in requiredFiles)
        {
            RequireKeyString(requiredFile, nameof(ModelInstallInfo.RequiredFiles));
        }
    }

    private sealed record CatalogDocument
    {
        public IReadOnlyList<ModelCatalogEntry?>? Models { get; init; }
    }
}
