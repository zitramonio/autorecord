using System.Windows;
using System.Diagnostics;
using System.Windows.Navigation;

namespace Autorecord.App.Dialogs;

public partial class HuggingFaceTokenDialog : Window
{
    public HuggingFaceTokenDialog(string modelDisplayName)
    {
        InitializeComponent();
        Loaded += (_, _) => TokenBox.Focus();
    }

    public string Token => TokenBox.Password.Trim();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Token))
        {
            System.Windows.MessageBox.Show(
                this,
                "Введите Hugging Face access token или отмените скачивание.",
                "Доступ Hugging Face",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void OpenLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
