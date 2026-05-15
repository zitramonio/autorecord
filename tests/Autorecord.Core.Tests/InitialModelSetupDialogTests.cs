namespace Autorecord.Core.Tests;

public sealed class InitialModelSetupDialogTests
{
    [Fact]
    public void InitialModelSetupDialogOffersSeparateModelDownloadsAndCancelText()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "Autorecord.App",
            "Dialogs",
            "InitialModelSetupDialog.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "Autorecord.App",
            "Dialogs",
            "InitialModelSetupDialog.xaml.cs"));

        Assert.Contains("Скачать модель транскрибации", xaml, StringComparison.Ordinal);
        Assert.Contains("Скачать модель разделения на спикеров", xaml, StringComparison.Ordinal);
        Assert.Contains("Транскрибация производиться не будет", xaml, StringComparison.Ordinal);
        Assert.Contains("вкладке &quot;Транскрибация&quot;", xaml, StringComparison.Ordinal);
        Assert.Contains("InitialModelSetupAction.DownloadAsr", codeBehind, StringComparison.Ordinal);
        Assert.Contains("InitialModelSetupAction.DownloadDiarization", codeBehind, StringComparison.Ordinal);
        Assert.Contains("InitialModelSetupAction.Cancel", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelSetupCancelledDialogShowsWarningAndOkButton()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "Autorecord.App",
            "Dialogs",
            "ModelSetupCancelledDialog.xaml"));

        Assert.Contains("Транскрибация производиться не будет", xaml, StringComparison.Ordinal);
        Assert.Contains("вкладке &quot;Транскрибация&quot;", xaml, StringComparison.Ordinal);
        Assert.Contains("Ок", xaml, StringComparison.Ordinal);
        Assert.Contains("IsDefault=\"True\"", xaml, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Autorecord.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
