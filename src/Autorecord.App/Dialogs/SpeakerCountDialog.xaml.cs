using System.Windows;
using System.Windows.Threading;

namespace Autorecord.App.Dialogs;

public partial class SpeakerCountDialog : Window
{
    private DispatcherTimer? _autoContinueTimer;

    public SpeakerCountDialog(int? initialSpeakerCount = null)
    {
        InitializeComponent();
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
        SpeakerCountBox.SelectedValue = initialSpeakerCount is >= 1 and <= 6
            ? initialSpeakerCount
            : null;
    }

    public int? SelectedSpeakerCount =>
        SpeakerCountBox.SelectedValue is int speakerCount ? speakerCount : null;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        _autoContinueTimer = new DispatcherTimer
        {
            Interval = SpeakerCountPrompt.AutoContinueTimeout
        };
        _autoContinueTimer.Tick += AutoContinueTimer_Tick;
        _autoContinueTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        StopAutoContinueTimer();
        base.OnClosed(e);
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void AutoContinueTimer_Tick(object? sender, EventArgs e)
    {
        StopAutoContinueTimer();
        DialogResult = true;
    }

    private void StopAutoContinueTimer()
    {
        if (_autoContinueTimer is null)
        {
            return;
        }

        _autoContinueTimer.Stop();
        _autoContinueTimer.Tick -= AutoContinueTimer_Tick;
        _autoContinueTimer = null;
    }

    private sealed record SpeakerCountOption(string DisplayName, int? Value);
}

public static class SpeakerCountPrompt
{
    public static TimeSpan AutoContinueTimeout { get; } = TimeSpan.FromMinutes(2);
}
