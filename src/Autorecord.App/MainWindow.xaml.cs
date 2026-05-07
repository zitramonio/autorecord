using System.ComponentModel;
using System.Windows;
using Autorecord.Core.Settings;
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
            Transcription = _settings.Transcription
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
}
