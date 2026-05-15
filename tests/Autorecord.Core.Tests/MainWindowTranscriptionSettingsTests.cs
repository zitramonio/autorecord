using Autorecord.App;
using Autorecord.App.Transcription;
using Autorecord.Core.Settings;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Autorecord.Core.Tests;

public sealed class MainWindowTranscriptionSettingsTests
{
    [Fact]
    public void MainWindowUsesLargerResizableDefaultSizeAndScrollableRecordingTab()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Autorecord.App", "MainWindow.xaml"));

        Assert.Contains("Width=\"760\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"720\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"640\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"520\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ResizeMode=\"CanResize\"", xaml, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordingTabUsesFramedSettingsBlocksAndIconButtons()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Autorecord.App", "MainWindow.xaml"));

        Assert.Contains("CalendarSettingsPanel", xaml, StringComparison.Ordinal);
        Assert.Contains("AutoStartRecordingFromCalendarBox", xaml, StringComparison.Ordinal);
        Assert.Contains("Запускать запись при начале события в календаре", xaml, StringComparison.Ordinal);
        Assert.Contains("AutoStopRecordingBox", xaml, StringComparison.Ordinal);
        Assert.Contains("Автоматически останавливать запись", xaml, StringComparison.Ordinal);
        Assert.Contains("NoAnswerStopPromptMinutesBox", xaml, StringComparison.Ordinal);
        Assert.Contains("NotificationsEnabledBox", xaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource RecordStartButtonStyle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource RecordStopButtonStyle}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain(">Начать запись<", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain(">Остановить запись<", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentTranscriptionCardDoesNotRenderPercentProgressLine()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Autorecord.App", "MainWindow.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Autorecord.App", "MainWindow.xaml.cs"));

        Assert.DoesNotContain("CurrentTranscriptionProgressBar", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentTranscriptionProgressText", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentTranscriptionProgressBar", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentTranscriptionProgressText", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void AsrModelSelectorIsHiddenAndSummaryIsShownInPublicRelease()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Autorecord.App", "MainWindow.xaml"));

        var asrCombo = Regex.Match(
            xaml,
            """<ComboBox x:Name="AsrModelBox"(?<body>.*?)SelectionChanged="AsrModelBox_SelectionChanged" />""",
            RegexOptions.Singleline);

        Assert.True(asrCombo.Success);
        Assert.Contains("Visibility=\"Collapsed\"", asrCombo.Groups["body"].Value, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AsrModelSummaryText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Модель транскрибации: GigaAM v3", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelManagementPanelDoesNotShowInternalStatusOrMaintenanceButtons()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Autorecord.App", "MainWindow.xaml"));
        var appCode = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Autorecord.App", "App.xaml.cs"));
        var codeBehind = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Autorecord.App", "MainWindow.xaml.cs"));

        Assert.DoesNotContain("Диаризация: Спикеры", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Статус моделей:", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain(">Удалить модели<", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain(">Проверить<", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain(">Папка моделей<", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Модель установлена и готова", appCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Модели установлены и готовы", appCode, StringComparison.Ordinal);
        Assert.Contains("!string.IsNullOrWhiteSpace(ModelDownloadDetailsText.Text)", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveAsrSelectionUsesSelectedUiModel()
    {
        var current = new TranscriptionSettings
        {
            SelectedAsrModelId = "saved-asr"
        };

        var selected = MainWindowTranscriptionSettings.ResolveAsrSelection("selected-asr", current);

        Assert.Equal("selected-asr", selected);
    }

    [Fact]
    public void NormalizeReleaseSettingsKeepsSelectedAsrModel()
    {
        var settings = new AppSettings
        {
            Transcription = new TranscriptionSettings
            {
                SelectedAsrModelId = "gigaam-v3-ru-quality"
            }
        };

        var normalized = NormalizeReleaseSettings(settings);

        Assert.Equal("gigaam-v3-ru-quality", normalized.Transcription.SelectedAsrModelId);
    }

    [Fact]
    public void NormalizeReleaseSettingsResetsParakeetSelectionForPublicRelease()
    {
        var settings = new AppSettings
        {
            Transcription = new TranscriptionSettings
            {
                SelectedAsrModelId = "sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8"
            }
        };

        var normalized = NormalizeReleaseSettings(settings);

        Assert.Equal("gigaam-v3-ru-quality", normalized.Transcription.SelectedAsrModelId);
    }

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

    [Fact]
    public void FormatSelectedModelStatusShowsAsrAndDiarizationStatuses()
    {
        var status = MainWindowTranscriptionSettings.FormatSelectedModelStatus(
            new ModelListItemViewModel("asr", "GigaAM v3", "asr", "Installed"),
            new ModelListItemViewModel("diarization", "Спикеры — Pyannote Community-1", "diarization", "NotInstalled"));

        Assert.Equal("Статус моделей: ASR — Installed; диаризация — NotInstalled", status);
    }

    [Fact]
    public void ShouldShowModelManagementHidesWhenBothModelsAreInstalled()
    {
        var show = MainWindowTranscriptionSettings.ShouldShowModelManagement(
            new ModelListItemViewModel("asr", "GigaAM v3", "asr", "Installed"),
            new ModelListItemViewModel("diarization", "Спикеры — Pyannote Community-1", "diarization", "Installed"),
            isDownloading: false);

        Assert.False(show);
    }

    [Fact]
    public void ShouldShowSpeakerModelDownloadHidesAfterDiarizationModelInstalled()
    {
        Assert.True(MainWindowTranscriptionSettings.ShouldShowSpeakerModelDownload(
            new ModelListItemViewModel("pyannote-community-1", "Спикеры — Pyannote Community-1", "diarization", "NotInstalled"),
            isDownloading: false));

        Assert.False(MainWindowTranscriptionSettings.ShouldShowSpeakerModelDownload(
            new ModelListItemViewModel("pyannote-community-1", "Спикеры — Pyannote Community-1", "diarization", "Installed"),
            isDownloading: false));
    }

    [Fact]
    public void SpeakerModelDownloadButtonTextIsPublicReleaseSpecific()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Autorecord.App", "MainWindow.xaml"));

        Assert.Contains("Скачать модель для разделения на спикеров", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain(">Скачать модели<", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AboutTabContainsModelLicensesAndRecordingNotice()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Autorecord.App", "MainWindow.xaml"));

        Assert.Contains("Header=\"О программе\"", xaml, StringComparison.Ordinal);
        Assert.Contains("GigaAM v3", xaml, StringComparison.Ordinal);
        Assert.Contains("MIT", xaml, StringComparison.Ordinal);
        Assert.Contains("Pyannote Community-1", xaml, StringComparison.Ordinal);
        Assert.Contains("CC BY 4.0", xaml, StringComparison.Ordinal);
        Assert.Contains("участники предупреждены", xaml, StringComparison.Ordinal);
        Assert.Contains("Hyperlink NavigateUri=\"https://github.com/salute-developers/GigaAM\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Hyperlink NavigateUri=\"https://huggingface.co/pyannote/speaker-diarization-community-1\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Предложения и пожелания можно отправить на", xaml, StringComparison.Ordinal);
        Assert.Contains("Hyperlink NavigateUri=\"mailto:zitramonio@proton.me\"", xaml, StringComparison.Ordinal);
        Assert.Contains("RequestNavigate=\"OpenLink_RequestNavigate\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultTranscriptFormatsOnlyEnableTxt()
    {
        var settings = new TranscriptionSettings();

        Assert.Equal([TranscriptOutputFormat.Txt], settings.OutputFormats);
    }

    [Theory]
    [InlineData("Installed", "NotInstalled", false)]
    [InlineData("NotInstalled", "Installed", false)]
    [InlineData("Installed", "Installed", true)]
    public void ShouldShowModelManagementShowsWhenActionIsNeeded(
        string asrStatus,
        string diarizationStatus,
        bool isDownloading)
    {
        var show = MainWindowTranscriptionSettings.ShouldShowModelManagement(
            new ModelListItemViewModel("asr", "Русский — качество", "asr", asrStatus),
            new ModelListItemViewModel("diarization", "Спикеры — Pyannote Community-1", "diarization", diarizationStatus),
            isDownloading);

        Assert.True(show);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Autorecord.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private static AppSettings NormalizeReleaseSettings(AppSettings settings)
    {
        var method = typeof(Autorecord.App.App).GetMethod(
            "NormalizeReleaseSettings",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (AppSettings)method.Invoke(null, [settings])!;
    }
}
