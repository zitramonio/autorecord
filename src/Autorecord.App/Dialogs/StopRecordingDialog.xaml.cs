using System.Windows;
using System.Windows.Threading;

namespace Autorecord.App.Dialogs;

public enum StopRecordingDialogResponse
{
    None = 0,
    Yes = 1,
    No = 2,
    Timeout = 3
}

public partial class StopRecordingDialog : Window
{
    private DispatcherTimer? _autoStopTimer;

    private readonly TimeSpan _autoStopTimeout = StopRecordingPrompt.DefaultAutoStopTimeout;

    public StopRecordingDialog()
    {
        InitializeComponent();
    }

    public StopRecordingDialog(TimeSpan autoStopTimeout)
        : this()
    {
        _autoStopTimeout = autoStopTimeout;
        PromptText.Text = $"На вводе и выводе тишина. Остановить запись? Если ответа нет {FormatMinutes(autoStopTimeout)}, запись остановится автоматически.";
    }

    public StopRecordingDialogResponse Response { get; private set; }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        _autoStopTimer = new DispatcherTimer
        {
            Interval = _autoStopTimeout
        };
        _autoStopTimer.Tick += AutoStopTimer_Tick;
        _autoStopTimer.Start();
    }

    private static string FormatMinutes(TimeSpan timeout)
    {
        var minutes = Math.Max(1, (int)Math.Round(timeout.TotalMinutes));
        return minutes == 1 ? "1 минуту" : $"{minutes} минут";
    }

    protected override void OnClosed(EventArgs e)
    {
        StopAutoStopTimer();
        base.OnClosed(e);
    }

    private void Yes_Click(object sender, RoutedEventArgs e)
    {
        Response = StopRecordingDialogResponse.Yes;
        DialogResult = true;
    }

    private void No_Click(object sender, RoutedEventArgs e)
    {
        Response = StopRecordingDialogResponse.No;
        DialogResult = false;
    }

    private void AutoStopTimer_Tick(object? sender, EventArgs e)
    {
        StopAutoStopTimer();
        Response = StopRecordingDialogResponse.Timeout;
        DialogResult = true;
    }

    private void StopAutoStopTimer()
    {
        if (_autoStopTimer is null)
        {
            return;
        }

        _autoStopTimer.Stop();
        _autoStopTimer.Tick -= AutoStopTimer_Tick;
        _autoStopTimer = null;
    }
}

public enum StopRecordingPromptAction
{
    Ignore = 0,
    Stop = 1,
    Delay = 2
}

public static class StopRecordingPrompt
{
    public static TimeSpan DefaultAutoStopTimeout { get; } = TimeSpan.FromMinutes(2);

    public static TimeSpan AutoStopTimeout => DefaultAutoStopTimeout;

    public static TimeSpan GetAutoStopTimeout(Autorecord.Core.Settings.AppSettings settings) =>
        TimeSpan.FromMinutes(settings.NoAnswerStopPromptMinutes);

    public static StopRecordingPromptAction ResolveAction(StopRecordingDialogResponse response)
    {
        return response switch
        {
            StopRecordingDialogResponse.Yes or StopRecordingDialogResponse.Timeout => StopRecordingPromptAction.Stop,
            StopRecordingDialogResponse.No => StopRecordingPromptAction.Delay,
            _ => StopRecordingPromptAction.Ignore
        };
    }
}
