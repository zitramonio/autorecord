using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using Autorecord.App.Transcription;
using Autorecord.Core.Settings;
using Autorecord.Core.Transcription.Jobs;
using Autorecord.Core.Transcription.Models;
using Forms = System.Windows.Forms;

namespace Autorecord.App;

public partial class MainWindow : Window
{
    private const string NoDiarizationModelId = "";

    private AppSettings _settings = new();
    private Guid? _currentTranscriptionJobId;
    private bool _isModelDownloadBusy;

    public MainWindow()
    {
        InitializeComponent();
        InitializeTranscriptionControls();
        LoadIntoForm(_settings);
    }

    public event EventHandler<AppSettings>? RefreshCalendarRequested;
    public event EventHandler<AppSettings>? SettingsSaved;
    public event EventHandler<AppSettings>? ManualRecordingStartRequested;
    public event EventHandler? ManualRecordingStopRequested;
    public event EventHandler? DownloadSelectedModelRequested;
    public event EventHandler? DownloadAsrModelRequested;
    public event EventHandler? DownloadDiarizationModelRequested;
    public event EventHandler? CancelModelDownloadRequested;
    public event EventHandler? DeleteSelectedModelRequested;
    public event EventHandler? ValidateSelectedModelRequested;
    public event EventHandler? OpenModelsFolderRequested;
    public event EventHandler? PickFileForTranscriptionRequested;
    public event EventHandler<TranscriptionJobActionRequestedEventArgs>? OpenTranscriptionJobTranscriptRequested;
    public event EventHandler<TranscriptionJobActionRequestedEventArgs>? OpenTranscriptionJobFolderRequested;
    public event EventHandler<TranscriptionJobActionRequestedEventArgs>? RetryTranscriptionJobRequested;
    public event EventHandler<TranscriptionJobActionRequestedEventArgs>? CancelTranscriptionJobRequested;
    public event EventHandler<TranscriptionJobActionRequestedEventArgs>? DeleteTranscriptionJobRequested;

    public bool AllowClose { get; set; }
    public string? SelectedModelId => SelectedAsrModelId;
    public string? SelectedAsrModelId => AsrModelBox.SelectedValue as string;
    public string? SelectedDiarizationModelId => AutorecordDefaults.DiarizationModelId;

    public void SetSettings(AppSettings settings)
    {
        _settings = settings;
        LoadIntoForm(settings);
    }

    public void SetStatus(string status)
    {
        StatusText.Text = status;
    }

    public void SetRecordingState(bool isRecording, string? details = null)
    {
        RecordingStatusText.Text = isRecording
            ? $"Идет запись: {details ?? "без названия"}"
            : details ?? "Запись не идет";
        StartRecordingButton.IsEnabled = !isRecording;
        StopRecordingButton.IsEnabled = isRecording;
    }

    public void SetModels(
        IReadOnlyList<ModelListItemViewModel> asrModels,
        IReadOnlyList<ModelListItemViewModel> diarizationModels)
    {
        var selectedModelId = _settings.Transcription.SelectedAsrModelId;
        AsrModelBox.ItemsSource = asrModels;
        AsrModelBox.SelectedValue = asrModels.Any(model => model.Id == selectedModelId)
            ? selectedModelId
            : asrModels.FirstOrDefault()?.Id;

        var selectedDiarizationModelId = AutorecordDefaults.DiarizationModelId;
        var diarizationOptions = diarizationModels.ToArray();
        DiarizationModelBox.ItemsSource = diarizationOptions;
        DiarizationModelBox.SelectedValue = diarizationOptions.Any(model => model.Id == selectedDiarizationModelId)
            ? selectedDiarizationModelId
            : diarizationOptions.FirstOrDefault()?.Id;

        UpdateSelectedModelStatus();
        UpdateAsrModelSummary();
        UpdateDiarizationControlsState();
        UpdateModelManagementVisibility();
    }

    public void SetSelectedModelStatus(string status)
    {
        SelectedModelStatusText.Text = status;
    }

    public void SetModelDownloadProgress(int percent)
    {
        ModelDownloadProgress.Value = Math.Clamp(percent, 0, 100);
    }

    public void SetModelDownloadProgress(ModelDownloadProgress progress)
    {
        ModelDownloadProgress.Value = progress.Percent;
        ModelDownloadDetailsText.Text = ModelDownloadProgressText.Format(progress);
    }

    public void SetModelDownloadStatus(string status)
    {
        ModelDownloadDetailsText.Text = status;
    }

    public void SetModelDownloadBusy(bool isDownloading)
    {
        _isModelDownloadBusy = isDownloading;
        DownloadAsrModelButton.IsEnabled = !isDownloading;
        DownloadDiarizationModelButton.IsEnabled = !isDownloading;
        CancelDownloadModelButton.IsEnabled = isDownloading;
        DeleteModelButton.IsEnabled = !isDownloading;
        ValidateModelButton.IsEnabled = !isDownloading;
        UpdateModelManagementVisibility();
    }

    public void SetCurrentTranscriptionJob(TranscriptionJob? job)
    {
        if (job is null)
        {
            _currentTranscriptionJobId = null;
            CurrentTranscriptionFileText.Text = "Файл не выбран";
            CurrentTranscriptionModelText.Text = "Модель: GigaAM v3; диаризация: Pyannote Community-1";
            CurrentTranscriptionStatusText.Text = "Статус: ожидание";
            CurrentTranscriptionStagesText.Text = string.Join(Environment.NewLine,
                "Чтение файла: ожидает",
                "Диаризация: ожидает",
                "Транскрибация: ожидает",
                "Сохранение транскрипта: ожидает");
            OpenCurrentTranscriptButton.IsEnabled = false;
            OpenCurrentFolderButton.IsEnabled = false;
            RetryCurrentTranscriptionButton.IsEnabled = false;
            CancelCurrentTranscriptionButton.IsEnabled = false;
            return;
        }

        var item = TranscriptionJobListItemViewModel.FromJob(job);
        _currentTranscriptionJobId = job.Id;
        CurrentTranscriptionFileText.Text = item.File;
        CurrentTranscriptionModelText.Text = $"Модель: {item.Model}; диаризация: {item.DiarizationModel}";
        CurrentTranscriptionStatusText.Text = $"Статус: {item.Status}";
        CurrentTranscriptionStagesText.Text = item.StageLines;
        OpenCurrentTranscriptButton.IsEnabled = item.CanOpenTranscript;
        OpenCurrentFolderButton.IsEnabled = item.CanOpenFolder;
        RetryCurrentTranscriptionButton.IsEnabled = item.CanRetry;
        CancelCurrentTranscriptionButton.IsEnabled = item.CanCancel;
    }

    private void InitializeTranscriptionControls()
    {
        SpeakerCountBox.ItemsSource = new SpeakerCountOption[]
        {
            new("Auto", null),
            new("1", 1),
            new("2", 2),
            new("3", 3),
            new("4", 4),
            new("5", 5),
            new("6", 6)
        };

        TranscriptOutputFolderModeBox.ItemsSource = new OutputFolderModeOption[]
        {
            new("Рядом с записью", TranscriptOutputFolderMode.SameAsRecording),
            new("Отдельная папка", TranscriptOutputFolderMode.CustomFolder)
        };
    }

    public bool TryGetCurrentSettings(out AppSettings settings, bool requireCalendarSettings = false)
    {
        return TryReadFromForm(out settings, requireCalendarSettings);
    }

    public bool TryGetCurrentTranscriptionSettings(out TranscriptionSettings settings)
    {
        return TryReadTranscriptionSettings(out settings);
    }

    private void RefreshCalendar_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadFromForm(out var settings, forceCalendarUrl: true))
        {
            return;
        }

        _settings = settings;
        RefreshCalendarRequested?.Invoke(this, _settings);
    }

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            SelectedPath = OutputFolderBox.Text
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            OutputFolderBox.Text = dialog.SelectedPath;
        }
    }

    private void ChooseTranscriptFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            SelectedPath = string.IsNullOrWhiteSpace(CustomTranscriptOutputFolderBox.Text)
                ? OutputFolderBox.Text
                : CustomTranscriptOutputFolderBox.Text
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            CustomTranscriptOutputFolderBox.Text = dialog.SelectedPath;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadFromForm(out var settings))
        {
            return;
        }

        _settings = settings;
        SettingsSaved?.Invoke(this, _settings);
    }

    private void StartRecording_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadFromForm(out var settings, requireCalendarSettings: false))
        {
            return;
        }

        _settings = settings;
        ManualRecordingStartRequested?.Invoke(this, _settings);
    }

    private void StopRecording_Click(object sender, RoutedEventArgs e)
    {
        ManualRecordingStopRequested?.Invoke(this, EventArgs.Empty);
    }

    private void DownloadModel_Click(object sender, RoutedEventArgs e)
    {
        DownloadSelectedModelRequested?.Invoke(this, EventArgs.Empty);
    }

    private void DownloadAsrModel_Click(object sender, RoutedEventArgs e)
    {
        DownloadAsrModelRequested?.Invoke(this, EventArgs.Empty);
    }

    private void DownloadDiarizationModel_Click(object sender, RoutedEventArgs e)
    {
        DownloadDiarizationModelRequested?.Invoke(this, EventArgs.Empty);
    }

    private void CancelDownloadModel_Click(object sender, RoutedEventArgs e)
    {
        CancelModelDownloadRequested?.Invoke(this, EventArgs.Empty);
    }

    private void DeleteModel_Click(object sender, RoutedEventArgs e)
    {
        DeleteSelectedModelRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ValidateModel_Click(object sender, RoutedEventArgs e)
    {
        ValidateSelectedModelRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OpenModelsFolder_Click(object sender, RoutedEventArgs e)
    {
        OpenModelsFolderRequested?.Invoke(this, EventArgs.Empty);
    }

    private void PickFileForTranscription_Click(object sender, RoutedEventArgs e)
    {
        PickFileForTranscriptionRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OpenTranscript_Click(object sender, RoutedEventArgs e)
    {
        RaiseJobAction(sender, OpenTranscriptionJobTranscriptRequested);
    }

    private void OpenTranscriptFolder_Click(object sender, RoutedEventArgs e)
    {
        RaiseJobAction(sender, OpenTranscriptionJobFolderRequested);
    }

    private void RetryTranscriptionJob_Click(object sender, RoutedEventArgs e)
    {
        RaiseJobAction(sender, RetryTranscriptionJobRequested);
    }

    private void CancelTranscriptionJob_Click(object sender, RoutedEventArgs e)
    {
        RaiseJobAction(sender, CancelTranscriptionJobRequested);
    }

    private void DeleteTranscriptionJob_Click(object sender, RoutedEventArgs e)
    {
        RaiseJobAction(sender, DeleteTranscriptionJobRequested);
    }

    private void OpenCurrentTranscript_Click(object sender, RoutedEventArgs e)
    {
        RaiseCurrentJobAction(OpenTranscriptionJobTranscriptRequested);
    }

    private void OpenCurrentFolder_Click(object sender, RoutedEventArgs e)
    {
        RaiseCurrentJobAction(OpenTranscriptionJobFolderRequested);
    }

    private void RetryCurrentTranscription_Click(object sender, RoutedEventArgs e)
    {
        RaiseCurrentJobAction(RetryTranscriptionJobRequested);
    }

    private void CancelCurrentTranscription_Click(object sender, RoutedEventArgs e)
    {
        RaiseCurrentJobAction(CancelTranscriptionJobRequested);
    }

    private void AsrModelBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateSelectedModelStatus();
        UpdateAsrModelSummary();
        UpdateModelManagementVisibility();
    }

    private void DiarizationModelBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateSelectedModelStatus();
        UpdateDiarizationControlsState();
    }

    private void TranscriptOutputFolderModeBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateTranscriptOutputFolderControlsState();
    }

    private void LoadIntoForm(AppSettings settings)
    {
        CalendarUrlBox.Text = settings.CalendarUrl;
        OutputFolderBox.Text = settings.OutputFolder;
        AutoStartRecordingFromCalendarBox.IsChecked = settings.AutoStartRecordingFromCalendar;
        TaggedModeBox.IsChecked = settings.RecordingMode == RecordingMode.TaggedEvents;
        EventTagBox.Text = settings.EventTag;
        AutoStopRecordingBox.IsChecked = settings.AutoStopRecordingOnSilence;
        SilenceMinutesBox.Text = settings.SilencePromptMinutes.ToString();
        RetryMinutesBox.Text = settings.RetryPromptMinutes.ToString();
        NoAnswerStopPromptMinutesBox.Text = settings.NoAnswerStopPromptMinutes.ToString();
        KeepMicrophoneReadyBox.IsChecked = settings.KeepMicrophoneReady;
        NotificationsEnabledBox.IsChecked = settings.NotificationsEnabled;
        StartupBox.IsChecked = settings.StartWithWindows;
        AutoTranscribeBox.IsChecked = true;
        if (AsrModelBox.ItemsSource is not null)
        {
            AsrModelBox.SelectedValue = settings.Transcription.SelectedAsrModelId;
            UpdateAsrModelSummary();
        }

        if (DiarizationModelBox.ItemsSource is not null)
        {
            DiarizationModelBox.SelectedValue = AutorecordDefaults.DiarizationModelId;
        }

        SpeakerCountBox.SelectedValue = settings.Transcription.NumSpeakers;
        TranscriptOutputFolderModeBox.SelectedValue = settings.Transcription.OutputFolderMode;
        CustomTranscriptOutputFolderBox.Text = settings.Transcription.CustomOutputFolder ?? "";
        TranscriptTxtFormatBox.IsChecked = settings.Transcription.OutputFormats.Contains(TranscriptOutputFormat.Txt);
        TranscriptMarkdownFormatBox.IsChecked = settings.Transcription.OutputFormats.Contains(TranscriptOutputFormat.Markdown);
        TranscriptSrtFormatBox.IsChecked = settings.Transcription.OutputFormats.Contains(TranscriptOutputFormat.Srt);
        TranscriptJsonFormatBox.IsChecked = settings.Transcription.OutputFormats.Contains(TranscriptOutputFormat.Json);
        UpdateDiarizationControlsState();
        UpdateTranscriptOutputFolderControlsState();
        UpdateAutoStopControlsState();
        SetRecordingState(false);
    }

    private bool TryReadFromForm(
        out AppSettings settings,
        bool requireCalendarSettings = true,
        bool forceCalendarUrl = false)
    {
        settings = _settings;
        var recordingMode = TaggedModeBox.IsChecked == true ? RecordingMode.TaggedEvents : RecordingMode.AllEvents;
        var autoStartRecordingFromCalendar = AutoStartRecordingFromCalendarBox.IsChecked == true;
        var autoStopRecordingOnSilence = AutoStopRecordingBox.IsChecked == true;
        var calendarUrl = CalendarUrlBox.Text.Trim();
        var calendarSettingsRequired = forceCalendarUrl || (requireCalendarSettings && autoStartRecordingFromCalendar);

        if ((calendarSettingsRequired && !TryReadRequiredText(CalendarUrlBox.Text, "iCal-ссылка", out calendarUrl)) ||
            !TryReadRequiredText(OutputFolderBox.Text, "Папка сохранения", out var outputFolder) ||
            (calendarSettingsRequired && recordingMode == RecordingMode.TaggedEvents && !TryReadRequiredText(EventTagBox.Text, "Метка события", out _)) ||
            !TryReadPositiveMinutes(SilenceMinutesBox.Text, "Минут тишины до запроса", out var silenceMinutes) ||
            !TryReadPositiveMinutes(RetryMinutesBox.Text, "Минут ожидания после ответа Нет", out var retryMinutes) ||
            !TryReadPositiveMinutes(NoAnswerStopPromptMinutesBox.Text, "Минут бездействия до автоответа", out var noAnswerMinutes))
        {
            return false;
        }

        if (!TryReadTranscriptionSettings(out var transcriptionSettings))
        {
            return false;
        }

        settings = new AppSettings
        {
            CalendarUrl = calendarUrl,
            OutputFolder = outputFolder,
            RecordingMode = recordingMode,
            EventTag = EventTagBox.Text.Trim(),
            AutoStartRecordingFromCalendar = autoStartRecordingFromCalendar,
            AutoStopRecordingOnSilence = autoStopRecordingOnSilence,
            SilencePromptMinutes = silenceMinutes,
            RetryPromptMinutes = retryMinutes,
            NoAnswerStopPromptMinutes = noAnswerMinutes,
            NotificationsEnabled = NotificationsEnabledBox.IsChecked == true,
            KeepMicrophoneReady = KeepMicrophoneReadyBox.IsChecked == true,
            StartWithWindows = StartupBox.IsChecked == true,
            Transcription = transcriptionSettings
        };

        return true;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose && System.Windows.Application.Current?.Dispatcher.HasShutdownStarted != true)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    private bool TryReadRequiredText(string text, string fieldName, out string value)
    {
        value = text.Trim();
        if (value.Length > 0)
        {
            return true;
        }

        System.Windows.MessageBox.Show(
            $"{fieldName}: укажите значение.",
            "Некорректные настройки",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return false;
    }

    private bool TryReadPositiveMinutes(string text, string fieldName, out int minutes)
    {
        if (int.TryParse(text.Trim(), out minutes) && minutes > 0)
        {
            return true;
        }

        System.Windows.MessageBox.Show(
            $"{fieldName}: укажите целое число больше 0.",
            "Некорректные настройки",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return false;
    }

    private bool TryReadTranscriptionSettings(out TranscriptionSettings settings)
    {
        settings = _settings.Transcription;
        var outputFolderMode = TranscriptOutputFolderModeBox.SelectedValue is TranscriptOutputFolderMode selectedOutputFolderMode
            ? selectedOutputFolderMode
            : _settings.Transcription.OutputFolderMode;
        var customOutputFolder = CustomTranscriptOutputFolderBox.Text.Trim();

        if (outputFolderMode == TranscriptOutputFolderMode.CustomFolder
            && !TryReadRequiredText(CustomTranscriptOutputFolderBox.Text, "Папка транскриптов", out customOutputFolder))
        {
            return false;
        }

        var outputFormats = ReadOutputFormats();
        if (outputFormats.Count == 0)
        {
            System.Windows.MessageBox.Show(
                "Форматы: выберите хотя бы один формат.",
                "Некорректные настройки",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        settings = _settings.Transcription with
        {
            AutoTranscribeAfterRecording = true,
            SelectedAsrModelId = MainWindowTranscriptionSettings.ResolveAsrSelection(
                AsrModelBox.SelectedValue as string,
                _settings.Transcription),
            EnableDiarization = true,
            SelectedDiarizationModelId = AutorecordDefaults.DiarizationModelId,
            NumSpeakers = SpeakerCountBox.SelectedValue is int numSpeakers ? numSpeakers : null,
            OutputFolderMode = outputFolderMode,
            CustomOutputFolder = string.IsNullOrWhiteSpace(customOutputFolder) ? null : customOutputFolder,
            OutputFormats = outputFormats
        };
        return true;
    }

    private IReadOnlyList<TranscriptOutputFormat> ReadOutputFormats()
    {
        var formats = new List<TranscriptOutputFormat>();
        if (TranscriptTxtFormatBox.IsChecked == true)
        {
            formats.Add(TranscriptOutputFormat.Txt);
        }

        if (TranscriptMarkdownFormatBox.IsChecked == true)
        {
            formats.Add(TranscriptOutputFormat.Markdown);
        }

        if (TranscriptSrtFormatBox.IsChecked == true)
        {
            formats.Add(TranscriptOutputFormat.Srt);
        }

        if (TranscriptJsonFormatBox.IsChecked == true)
        {
            formats.Add(TranscriptOutputFormat.Json);
        }

        return formats;
    }

    private void UpdateSelectedModelStatus()
    {
        SelectedModelStatusText.Text = MainWindowTranscriptionSettings.FormatSelectedModelStatus(
            AsrModelBox.SelectedItem as ModelListItemViewModel,
            DiarizationModelBox.SelectedItem as ModelListItemViewModel);
    }

    private void UpdateAsrModelSummary()
    {
        var asrModel = AsrModelBox.SelectedItem as ModelListItemViewModel;
        AsrModelSummaryText.Text = $"Модель транскрибации: {asrModel?.DisplayName ?? "GigaAM v3"}";
    }

    private void UpdateDiarizationControlsState()
    {
        SpeakerCountBox.IsEnabled = true;
    }

    private void UpdateTranscriptOutputFolderControlsState()
    {
        var customFolderSelected = TranscriptOutputFolderModeBox.SelectedValue is TranscriptOutputFolderMode.CustomFolder;
        CustomTranscriptOutputFolderBox.IsEnabled = customFolderSelected;
        ChooseTranscriptFolderButton.IsEnabled = customFolderSelected;
    }

    private void CalendarAutoStartBox_Changed(object sender, RoutedEventArgs e)
    {
    }

    private void AutoStopRecordingBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateAutoStopControlsState();
    }

    private void UpdateAutoStopControlsState()
    {
        var enabled = AutoStopRecordingBox.IsChecked == true;
        SilenceMinutesBox.IsEnabled = enabled;
        RetryMinutesBox.IsEnabled = enabled;
        NoAnswerStopPromptMinutesBox.IsEnabled = enabled;
    }

    private void UpdateModelManagementVisibility()
    {
        var visibility = MainWindowTranscriptionSettings.ShouldShowModelManagement(
            AsrModelBox.SelectedItem as ModelListItemViewModel,
            DiarizationModelBox.SelectedItem as ModelListItemViewModel,
            _isModelDownloadBusy)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ModelStatusPanel.Visibility = Visibility.Visible;
        ModelActionsPanel.Visibility = visibility;
        DownloadAsrModelButton.Visibility = MainWindowTranscriptionSettings.ShouldShowAsrModelDownloadButton(
            AsrModelBox.SelectedItem as ModelListItemViewModel,
            _isModelDownloadBusy)
            ? Visibility.Visible
            : Visibility.Collapsed;
        DownloadDiarizationModelButton.Visibility = MainWindowTranscriptionSettings.ShouldShowDiarizationModelDownloadButton(
            DiarizationModelBox.SelectedItem as ModelListItemViewModel,
            _isModelDownloadBusy)
            ? Visibility.Visible
            : Visibility.Collapsed;
        CancelDownloadModelButton.Visibility = _isModelDownloadBusy
            ? Visibility.Visible
            : Visibility.Collapsed;
        ModelDownloadProgress.Visibility = _isModelDownloadBusy
            ? Visibility.Visible
            : Visibility.Collapsed;
        ModelDownloadDetailsText.Visibility = _isModelDownloadBusy || !string.IsNullOrWhiteSpace(ModelDownloadDetailsText.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OpenLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private sealed record SpeakerCountOption(string DisplayName, int? Value);

    private sealed record OutputFolderModeOption(string DisplayName, TranscriptOutputFolderMode Value);

    private void RaiseJobAction(
        object sender,
        EventHandler<TranscriptionJobActionRequestedEventArgs>? handler)
    {
        if (sender is not FrameworkElement { DataContext: TranscriptionJobListItemViewModel job })
        {
            return;
        }

        handler?.Invoke(this, new TranscriptionJobActionRequestedEventArgs(job.Id));
    }

    private void RaiseCurrentJobAction(EventHandler<TranscriptionJobActionRequestedEventArgs>? handler)
    {
        if (_currentTranscriptionJobId is not { } jobId)
        {
            return;
        }

        handler?.Invoke(this, new TranscriptionJobActionRequestedEventArgs(jobId));
    }
}

public static class MainWindowTranscriptionSettings
{
    public sealed record PreparedForInstalledModels(
        bool CanTranscribe,
        TranscriptionSettings Settings);

    public static (bool EnableDiarization, string SelectedDiarizationModelId) ResolveDiarizationSelection(
        string? selectedDiarizationModelId,
        TranscriptionSettings currentSettings)
    {
        if (selectedDiarizationModelId is null)
        {
            return (currentSettings.EnableDiarization, currentSettings.SelectedDiarizationModelId);
        }

        return (!string.IsNullOrWhiteSpace(selectedDiarizationModelId), selectedDiarizationModelId);
    }

    public static string ResolveAsrSelection(
        string? selectedAsrModelId,
        TranscriptionSettings currentSettings)
    {
        return string.IsNullOrWhiteSpace(selectedAsrModelId)
            ? currentSettings.SelectedAsrModelId
            : selectedAsrModelId;
    }

    public static IReadOnlyList<string> ResolveSelectedModelIdsForAction(
        string? selectedAsrModelId,
        string? selectedDiarizationModelId,
        TranscriptionSettings currentSettings)
    {
        var modelIds = new List<string>();
        var asrModelId = ResolveAsrSelection(selectedAsrModelId, currentSettings);
        if (!string.IsNullOrWhiteSpace(asrModelId))
        {
            modelIds.Add(asrModelId);
        }

        var (enableDiarization, diarizationModelId) = ResolveDiarizationSelection(
            selectedDiarizationModelId,
            currentSettings);
        if (enableDiarization && !string.IsNullOrWhiteSpace(diarizationModelId))
        {
            modelIds.Add(diarizationModelId);
        }

        return modelIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static string FormatSelectedModelStatus(
        ModelListItemViewModel? asrModel,
        ModelListItemViewModel? diarizationModel)
    {
        if (asrModel is null)
        {
            return "Модель не выбрана";
        }

        var diarizationStatus = diarizationModel is null || string.IsNullOrWhiteSpace(diarizationModel.Id)
            ? "выключена"
            : diarizationModel.Status;

        return $"Статус моделей: ASR — {asrModel.Status}; диаризация — {diarizationStatus}";
    }

    public static bool ShouldShowModelManagement(
        ModelListItemViewModel? asrModel,
        ModelListItemViewModel? diarizationModel,
        bool isDownloading)
    {
        if (isDownloading)
        {
            return true;
        }

        return !IsInstalled(asrModel) || !IsInstalled(diarizationModel);
    }

    public static bool ShouldShowSpeakerModelDownload(
        ModelListItemViewModel? diarizationModel,
        bool isDownloading)
    {
        return !isDownloading && !IsInstalled(diarizationModel);
    }

    public static bool ShouldShowAsrModelDownloadButton(
        ModelListItemViewModel? asrModel,
        bool isDownloading)
    {
        return !isDownloading && !IsInstalled(asrModel);
    }

    public static bool ShouldShowDiarizationModelDownloadButton(
        ModelListItemViewModel? diarizationModel,
        bool isDownloading)
    {
        return !isDownloading && !IsInstalled(diarizationModel);
    }

    public static PreparedForInstalledModels PrepareForInstalledModels(
        TranscriptionSettings settings,
        bool isAsrInstalled,
        bool isDiarizationInstalled)
    {
        if (!isAsrInstalled)
        {
            return new PreparedForInstalledModels(false, settings);
        }

        if (!settings.EnableDiarization || isDiarizationInstalled)
        {
            return new PreparedForInstalledModels(true, settings);
        }

        return new PreparedForInstalledModels(
            true,
            settings with
            {
                EnableDiarization = false,
                NumSpeakers = null
            });
    }

    public static TranscriptionSettings PreparePipelineSettingsForJob(
        TranscriptionSettings currentSettings,
        string asrModelId,
        string? diarizationModelId)
    {
        return currentSettings with
        {
            SelectedAsrModelId = asrModelId,
            EnableDiarization = !string.IsNullOrWhiteSpace(diarizationModelId),
            SelectedDiarizationModelId = diarizationModelId ?? "",
            NumSpeakers = string.IsNullOrWhiteSpace(diarizationModelId)
                ? null
                : currentSettings.NumSpeakers
        };
    }

    private static bool IsInstalled(ModelListItemViewModel? model)
    {
        return string.Equals(model?.Status, "Installed", StringComparison.OrdinalIgnoreCase);
    }
}
