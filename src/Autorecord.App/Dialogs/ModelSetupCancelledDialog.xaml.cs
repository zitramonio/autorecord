using System.Windows;

namespace Autorecord.App.Dialogs;

public partial class ModelSetupCancelledDialog : Window
{
    public ModelSetupCancelledDialog()
    {
        InitializeComponent();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
