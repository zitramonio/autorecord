using Autorecord.App;
using Autorecord.Core.Settings;

namespace Autorecord.Core.Tests;

public sealed class MainWindowTranscriptionSettingsTests
{
    [Fact]
    public void ResolveDiarizationSelectionKeepsCurrentSettingsWhenSelectionIsNull()
    {
        var current = new TranscriptionSettings
        {
            EnableDiarization = true,
            SelectedDiarizationModelId = "diarization-fast"
        };

        var (enableDiarization, selectedModelId) =
            MainWindowTranscriptionSettings.ResolveDiarizationSelection(null, current);

        Assert.True(enableDiarization);
        Assert.Equal("diarization-fast", selectedModelId);
    }

    [Fact]
    public void ResolveDiarizationSelectionTreatsEmptySelectionAsExplicitNoDiarization()
    {
        var current = new TranscriptionSettings
        {
            EnableDiarization = true,
            SelectedDiarizationModelId = "diarization-fast"
        };

        var (enableDiarization, selectedModelId) =
            MainWindowTranscriptionSettings.ResolveDiarizationSelection("", current);

        Assert.False(enableDiarization);
        Assert.Equal("", selectedModelId);
    }

    [Fact]
    public void ResolveSelectedModelIdsForActionIncludesAsrAndEnabledDiarization()
    {
        var current = new TranscriptionSettings
        {
            EnableDiarization = true,
            SelectedAsrModelId = "saved-asr",
            SelectedDiarizationModelId = "saved-diarization"
        };

        var modelIds = MainWindowTranscriptionSettings.ResolveSelectedModelIdsForAction(
            "selected-asr",
            "selected-diarization",
            current);

        Assert.Equal(["selected-asr", "selected-diarization"], modelIds);
    }

    [Fact]
    public void ResolveSelectedModelIdsForActionExcludesNoDiarizationSentinel()
    {
        var current = new TranscriptionSettings
        {
            EnableDiarization = true,
            SelectedAsrModelId = "saved-asr",
            SelectedDiarizationModelId = "saved-diarization"
        };

        var modelIds = MainWindowTranscriptionSettings.ResolveSelectedModelIdsForAction(
            "selected-asr",
            "",
            current);

        Assert.Equal(["selected-asr"], modelIds);
    }

    [Fact]
    public void ResolveSelectedModelIdsForActionFallsBackToCurrentSettingsWhenUiSelectionIsNull()
    {
        var current = new TranscriptionSettings
        {
            EnableDiarization = true,
            SelectedAsrModelId = "saved-asr",
            SelectedDiarizationModelId = "saved-diarization"
        };

        var modelIds = MainWindowTranscriptionSettings.ResolveSelectedModelIdsForAction(null, null, current);

        Assert.Equal(["saved-asr", "saved-diarization"], modelIds);
    }
}
