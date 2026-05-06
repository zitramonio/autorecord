using System.Windows;

namespace Autorecord.App.Dialogs;

public enum StopRecordingDialogResponse
{
    None = 0,
    Yes = 1,
    No = 2
}

public partial class StopRecordingDialog : Window
{
    public StopRecordingDialog()
    {
        InitializeComponent();
    }

    public StopRecordingDialogResponse Response { get; private set; }

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
}
