using System.ComponentModel;
using System.Windows;
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
    public string? SelectedDiarizationModelId => DiarizationModelBox.SelectedValue as string;

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
        var selectedModelId = SelectedAsrModelId ?? _settings.Transcription.SelectedAsrModelId;
        AsrModelBox.ItemsSource = asrModels;
        AsrModelBox.SelectedValue = asrModels.Any(model => model.Id == selectedModelId)
            ? selectedModelId
            : asrModels.FirstOrDefault()?.Id;

        var selectedDiarizationModelId = SelectedDiarizationModelId
            ?? (_settings.Transcription.EnableDiarization
                ? _settings.Transcription.SelectedDiarizationModelId
                : NoDiarizationModelId);
        var diarizationOptions = new[]
            {
                new ModelListItemViewModel(NoDiarizationModelId, "Без разделения по спикерам", "diarization", "")
            }
            .Concat(diarizationModels)
            .ToArray();
        DiarizationModelBox.ItemsSource = diarizationOptions;
        DiarizationModelBox.SelectedValue = diarizationOptions.Any(model => model.Id == selectedDiarizationModelId)
            ? selectedDiarizationModelId
            : NoDiarizationModelId;

        UpdateSelectedModelStatus();
        UpdateDiarizationControlsState();
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
        DownloadModelButton.IsEnabled = !isDownloading;
        CancelDownloadModelButton.IsEnabled = isDownloading;
        DeleteModelButton.IsEnabled = !isDownloading;
        ValidateModelButton.IsEnabled = !isDownloading;
    }

    public void SetTranscriptionJobs(IReadOnlyList<TranscriptionJob> jobs)
    {
        TranscriptionJobsGrid.ItemsSource = jobs
            .OrderByDescending(job => job.CreatedAt)
            .Select(TranscriptionJobListItemViewModel.FromJob)
            .ToArray();
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

    private void RefreshCalendar_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadFromForm(out var settings))
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

    private void AsrModelBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateSelectedModelStatus();
    }

    private void DiarizationModelBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
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
        TaggedModeBox.IsChecked = settings.RecordingMode == RecordingMode.TaggedEvents;
        EventTagBox.Text = settings.EventTag;
        SilenceMinutesBox.Text = settings.SilencePromptMinutes.ToString();
        RetryMinutesBox.Text = settings.RetryPromptMinutes.ToString();
        KeepMicrophoneReadyBox.IsChecked = settings.KeepMicrophoneReady;
        StartupBox.IsChecked = settings.StartWithWindows;
        AutoTranscribeBox.IsChecked = settings.Transcription.AutoTranscribeAfterRecording;
        if (AsrModelBox.ItemsSource is not null)
        {
            AsrModelBox.SelectedValue = settings.Transcription.SelectedAsrModelId;
        }

        if (DiarizationModelBox.ItemsSource is not null)
        {
            DiarizationModelBox.SelectedValue = settings.Transcription.EnableDiarization
                ? settings.Transcription.SelectedDiarizationModelId
                : NoDiarizationModelId;
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
        SetRecordingState(false);
    }

    private bool TryReadFromForm(out AppSettings settings, bool requireCalendarSettings = true)
    {
        settings = _settings;
        var recordingMode = TaggedModeBox.IsChecked == true ? RecordingMode.TaggedEvents : RecordingMode.AllEvents;
        var calendarUrl = CalendarUrlBox.Text.Trim();

        if ((requireCalendarSettings && !TryReadRequiredText(CalendarUrlBox.Text, "iCal-ссылка", out calendarUrl)) ||
            !TryReadRequiredText(OutputFolderBox.Text, "Папка сохранения", out var outputFolder) ||
            (requireCalendarSettings && recordingMode == RecordingMode.TaggedEvents && !TryReadRequiredText(EventTagBox.Text, "Метка события", out _)) ||
            !TryReadPositiveMinutes(SilenceMinutesBox.Text, "Минут тишины до запроса", out var silenceMinutes) ||
            !TryReadPositiveMinutes(RetryMinutesBox.Text, "Минут ожидания после ответа Нет", out var retryMinutes))
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
            SilencePromptMinutes = silenceMinutes,
            RetryPromptMinutes = retryMinutes,
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

        var selectedAsrModelId = SelectedAsrModelId;
        var (enableDiarization, selectedDiarizationModelId) =
            MainWindowTranscriptionSettings.ResolveDiarizationSelection(SelectedDiarizationModelId, _settings.Transcription);
        settings = _settings.Transcription with
        {
            AutoTranscribeAfterRecording = AutoTranscribeBox.IsChecked == true,
            SelectedAsrModelId = string.IsNullOrWhiteSpace(selectedAsrModelId)
                ? _settings.Transcription.SelectedAsrModelId
                : selectedAsrModelId,
            EnableDiarization = enableDiarization,
            SelectedDiarizationModelId = selectedDiarizationModelId,
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
        if (AsrModelBox.SelectedItem is ModelListItemViewModel model)
        {
            SelectedModelStatusText.Text = $"Статус модели: {model.Status}";
            return;
        }

        SelectedModelStatusText.Text = "Модель не выбрана";
    }

    private void UpdateDiarizationControlsState()
    {
        var enabled = !string.IsNullOrWhiteSpace(SelectedDiarizationModelId);
        SpeakerCountBox.IsEnabled = enabled;
    }

    private void UpdateTranscriptOutputFolderControlsState()
    {
        var customFolderSelected = TranscriptOutputFolderModeBox.SelectedValue is TranscriptOutputFolderMode.CustomFolder;
        CustomTranscriptOutputFolderBox.IsEnabled = customFolderSelected;
        ChooseTranscriptFolderButton.IsEnabled = customFolderSelected;
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
}

public static class MainWindowTranscriptionSettings
{
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

    public static IReadOnlyList<string> ResolveSelectedModelIdsForAction(
        string? selectedAsrModelId,
        string? selectedDiarizationModelId,
        TranscriptionSettings currentSettings)
    {
        var modelIds = new List<string>();
        var asrModelId = string.IsNullOrWhiteSpace(selectedAsrModelId)
            ? currentSettings.SelectedAsrModelId
            : selectedAsrModelId;
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
}
