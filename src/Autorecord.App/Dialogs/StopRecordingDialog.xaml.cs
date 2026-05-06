using System.Windows;

namespace Autorecord.App.Dialogs;

public partial class StopRecordingDialog : Window
{
    public StopRecordingDialog()
    {
        InitializeComponent();
    }

    private void Yes_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void No_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
