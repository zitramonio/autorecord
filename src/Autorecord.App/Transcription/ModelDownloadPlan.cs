using Autorecord.Core.Transcription.Models;

namespace Autorecord.App.Transcription;

public static class ModelDownloadPlan
{
    public static IReadOnlyList<ModelCatalogEntry> Create(
        ModelCatalogEntry asrModel,
        ModelInstallStatus asrStatus,
        bool enableDiarization,
        ModelCatalogEntry? diarizationModel,
        ModelInstallStatus? diarizationStatus)
    {
        ArgumentNullException.ThrowIfNull(asrModel);

        var models = new List<ModelCatalogEntry>();
        if (asrStatus != ModelInstallStatus.Installed)
        {
            models.Add(asrModel);
        }

        if (enableDiarization
            && diarizationModel is not null
            && diarizationStatus != ModelInstallStatus.Installed)
        {
            models.Add(diarizationModel);
        }

        return models;
    }
}
