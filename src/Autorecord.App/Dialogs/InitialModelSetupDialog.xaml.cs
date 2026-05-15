using System.Windows;

namespace Autorecord.App.Dialogs;

public enum InitialModelSetupAction
{
    Cancel = 0,
    DownloadAsr = 1,
    DownloadDiarization = 2
}

public partial class InitialModelSetupDialog : Window
{
    public InitialModelSetupDialog(bool asrMissing, bool diarizationMissing)
    {
        InitializeComponent();
        DownloadAsrButton.IsEnabled = asrMissing;
        DownloadDiarizationButton.IsEnabled = diarizationMissing;
    }

    public InitialModelSetupAction SelectedAction { get; private set; } = InitialModelSetupAction.Cancel;

    private void DownloadAsr_Click(object sender, RoutedEventArgs e)
    {
        SelectedAction = InitialModelSetupAction.DownloadAsr;
        DialogResult = true;
    }

    private void DownloadDiarization_Click(object sender, RoutedEventArgs e)
    {
        SelectedAction = InitialModelSetupAction.DownloadDiarization;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        SelectedAction = InitialModelSetupAction.Cancel;
        DialogResult = false;
    }
}
