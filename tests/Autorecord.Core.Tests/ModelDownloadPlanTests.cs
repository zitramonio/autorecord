using Autorecord.App.Transcription;
using Autorecord.Core.Transcription.Models;

namespace Autorecord.Core.Tests;

public sealed class ModelDownloadPlanTests
{
    [Fact]
    public void CreateSkipsInstalledAsrAndIncludesMissingDiarization()
    {
        var asr = CreateModel("gigaam-v3-ru-quality", "Русский — качество", "asr");
        var diarization = CreateModel("sherpa-diarization-pyannote-fast", "Спикеры — быстро", "diarization");

        var models = ModelDownloadPlan.Create(
            asr,
            ModelInstallStatus.Installed,
            enableDiarization: true,
            diarization,
            ModelInstallStatus.NotInstalled);

        var model = Assert.Single(models);
        Assert.Equal("sherpa-diarization-pyannote-fast", model.Id);
    }

    [Fact]
    public void CreateReturnsEmptyWhenSelectedModelsAreInstalled()
    {
        var asr = CreateModel("gigaam-v3-ru-quality", "Русский — качество", "asr");
        var diarization = CreateModel("sherpa-diarization-pyannote-fast", "Спикеры — быстро", "diarization");

        var models = ModelDownloadPlan.Create(
            asr,
            ModelInstallStatus.Installed,
            enableDiarization: true,
            diarization,
            ModelInstallStatus.Installed);

        Assert.Empty(models);
    }

    private static ModelCatalogEntry CreateModel(string id, string displayName, string type)
    {
        return new ModelCatalogEntry
        {
            Id = id,
            DisplayName = displayName,
            Type = type
        };
    }
}
