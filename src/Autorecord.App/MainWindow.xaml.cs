using System.ComponentModel;
using System.Windows;
using Autorecord.App.Transcription;
using Autorecord.Core.Settings;
using Autorecord.Core.Transcription.Jobs;
using Forms = System.Windows.Forms;

namespace Autorecord.App;

public partial class MainWindow : Window
{
    private AppSettings _settings = new();

    public MainWindow()
    {
        InitializeComponent();
        LoadIntoForm(_settings);
    }

    public event EventHandler<AppSettings>? RefreshCalendarRequested;
    public event EventHandler<AppSettings>? SettingsSaved;
    public event EventHandler<AppSettings>? ManualRecordingStartRequested;
    public event EventHandler? ManualRecordingStopRequested;
    public event EventHandler? DownloadSelectedModelRequested;
    public event EventHandler? DeleteSelectedModelRequested;
    public event EventHandler? ValidateSelectedModelRequested;
    public event EventHandler? OpenModelsFolderRequested;
    public event EventHandler? PickFileForTranscriptionRequested;

    public bool AllowClose { get; set; }
    public string? SelectedModelId => AsrModelBox.SelectedValue as string;

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

    public void SetModels(IReadOnlyList<ModelListItemViewModel> models)
    {
        var selectedModelId = SelectedModelId ?? _settings.Transcription.SelectedAsrModelId;
        AsrModelBox.ItemsSource = models;
        AsrModelBox.SelectedValue = models.Any(model => model.Id == selectedModelId)
            ? selectedModelId
            : models.FirstOrDefault()?.Id;
        UpdateSelectedModelStatus();
    }

    public void SetSelectedModelStatus(string status)
    {
        SelectedModelStatusText.Text = status;
    }

    public void SetModelDownloadProgress(int percent)
    {
        ModelDownloadProgress.Value = Math.Clamp(percent, 0, 100);
    }

    public void SetTranscriptionJobs(IReadOnlyList<TranscriptionJob> jobs)
    {
        TranscriptionJobsGrid.ItemsSource = jobs
            .OrderByDescending(job => job.CreatedAt)
            .Select(job => new
            {
                File = job.InputFilePath,
                Model = job.AsrModelId,
                Status = FormatJobStatus(job),
                Progress = $"{job.ProgressPercent}%",
                CreatedAt = job.CreatedAt.ToLocalTime().ToString("g"),
                CompletedAt = job.FinishedAt?.ToLocalTime().ToString("g") ?? ""
            })
            .ToArray();
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

    private void AsrModelBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateSelectedModelStatus();
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
        DiarizationModeBox.IsChecked = settings.Transcription.EnableDiarization;
        if (AsrModelBox.ItemsSource is not null)
        {
            AsrModelBox.SelectedValue = settings.Transcription.SelectedAsrModelId;
        }

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
            Transcription = ReadTranscriptionSettings()
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

    private TranscriptionSettings ReadTranscriptionSettings()
    {
        var selectedAsrModelId = SelectedModelId;
        return _settings.Transcription with
        {
            AutoTranscribeAfterRecording = AutoTranscribeBox.IsChecked == true,
            SelectedAsrModelId = string.IsNullOrWhiteSpace(selectedAsrModelId)
                ? _settings.Transcription.SelectedAsrModelId
                : selectedAsrModelId,
            EnableDiarization = DiarizationModeBox.IsChecked == true
        };
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

    private static string FormatJobStatus(TranscriptionJob job)
    {
        return job.Status == TranscriptionJobStatus.Failed && !string.IsNullOrWhiteSpace(job.ErrorMessage)
            ? $"{job.Status}: {job.ErrorMessage}"
            : job.Status.ToString();
    }
}
