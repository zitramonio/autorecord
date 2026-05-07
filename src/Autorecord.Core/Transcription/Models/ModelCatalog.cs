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

        var models = document?.Models ?? [];
        Validate(models);
        return new ModelCatalog(models);
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

    private static void Validate(IReadOnlyList<ModelCatalogEntry> models)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in models)
        {
            RequireNonEmpty(model.Id, nameof(ModelCatalogEntry.Id));
            RequireNonEmpty(model.DisplayName, nameof(ModelCatalogEntry.DisplayName));
            RequireNonEmpty(model.Type, nameof(ModelCatalogEntry.Type));
            RequireNonEmpty(model.Engine, nameof(ModelCatalogEntry.Engine));
            if (model.Install is null)
            {
                throw new InvalidOperationException($"Model catalog field '{nameof(ModelCatalogEntry.Install)}' must not be empty.");
            }

            RequireNonEmpty(model.Install.TargetFolder, nameof(ModelInstallInfo.TargetFolder));

            if (!ids.Add(model.Id))
            {
                throw new InvalidOperationException($"Duplicate model id '{model.Id}' in catalog.");
            }
        }
    }

    private static void RequireNonEmpty(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Model catalog field '{name}' must not be empty.");
        }
    }

    private sealed record CatalogDocument
    {
        public IReadOnlyList<ModelCatalogEntry> Models { get; init; } = [];
    }
}
