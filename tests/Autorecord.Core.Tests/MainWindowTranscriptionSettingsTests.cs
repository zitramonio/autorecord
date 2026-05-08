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
}
