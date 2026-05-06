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

    private void LoadIntoForm(AppSettings settings)
    {
        CalendarUrlBox.Text = settings.CalendarUrl;
        OutputFolderBox.Text = settings.OutputFolder;
        TaggedModeBox.IsChecked = settings.RecordingMode == RecordingMode.TaggedEvents;
        EventTagBox.Text = settings.EventTag;
        SilenceMinutesBox.Text = settings.SilencePromptMinutes.ToString();
        RetryMinutesBox.Text = settings.RetryPromptMinutes.ToString();
        StartupBox.IsChecked = settings.StartWithWindows;
    }

    private bool TryReadFromForm(out AppSettings settings)
    {
        settings = _settings;
        var recordingMode = TaggedModeBox.IsChecked == true ? RecordingMode.TaggedEvents : RecordingMode.AllEvents;

        if (!TryReadRequiredText(CalendarUrlBox.Text, "iCal-ссылка", out var calendarUrl) ||
            !TryReadRequiredText(OutputFolderBox.Text, "Папка сохранения", out var outputFolder) ||
            (recordingMode == RecordingMode.TaggedEvents && !TryReadRequiredText(EventTagBox.Text, "Метка события", out _)) ||
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
            StartWithWindows = StartupBox.IsChecked == true
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
