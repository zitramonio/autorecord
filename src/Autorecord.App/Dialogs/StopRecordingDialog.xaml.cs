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

    public StopRecordingDialog()
    {
        InitializeComponent();
    }

    public StopRecordingDialogResponse Response { get; private set; }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        _autoStopTimer = new DispatcherTimer
        {
            Interval = StopRecordingPrompt.AutoStopTimeout
        };
        _autoStopTimer.Tick += AutoStopTimer_Tick;
        _autoStopTimer.Start();
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
    public static TimeSpan AutoStopTimeout { get; } = TimeSpan.FromMinutes(2);

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
