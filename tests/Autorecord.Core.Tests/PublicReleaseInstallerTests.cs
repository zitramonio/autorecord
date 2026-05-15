namespace Autorecord.Core.Tests;

public sealed class PublicReleaseInstallerTests
{
    [Fact]
    public void PackageInstallerBundlesGigaAmButNotPyannote()
    {
        var repositoryRoot = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(repositoryRoot, "scripts", "package-installer.ps1"));

        Assert.Contains("gigaam-v3-ru-quality", script, StringComparison.Ordinal);
        Assert.DoesNotContain("pyannote-community-1", script, StringComparison.Ordinal);
        Assert.DoesNotContain("pytorch_model.bin", script, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallerShowsAgreementBeforeExtractingPayload()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(repositoryRoot, "tools", "installer", "AutorecordInstaller.cs"));

        Assert.Contains("ShowWizard", source, StringComparison.Ordinal);
        Assert.Contains("Лицензионное соглашение", source, StringComparison.Ordinal);
        Assert.Contains("Папка установки", source, StringComparison.Ordinal);
        Assert.Contains("Установка", source, StringComparison.Ordinal);
        Assert.Contains("Установка завершена", source, StringComparison.Ordinal);
        Assert.Contains("Открыть Autorecord", source, StringComparison.Ordinal);
        Assert.Contains("Я согласен", source, StringComparison.Ordinal);
        Assert.Contains("GigaAM v3", source, StringComparison.Ordinal);
        Assert.Contains("Pyannote Community-1", source, StringComparison.Ordinal);
        Assert.Contains("участников", source, StringComparison.Ordinal);
        Assert.Contains("ProgressBar", source, StringComparison.Ordinal);

        var agreementIndex = source.IndexOf("ShowWizard", StringComparison.Ordinal);
        var payloadIndex = source.IndexOf("OpenPayloadStream", StringComparison.Ordinal);
        Assert.True(agreementIndex >= 0 && payloadIndex > agreementIndex);
    }

    [Fact]
    public void InstallerWizardPagesHaveRealInitialSizeBeforeAnchoredControlsAreAdded()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(repositoryRoot, "tools", "installer", "AutorecordInstaller.cs"));

        Assert.Contains("page.Size = WizardPageSize", source, StringComparison.Ordinal);
        Assert.Contains("new Size(620, 376)", source, StringComparison.Ordinal);
        Assert.Contains("_agreeBox.Text = \"Я согласен", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallerNormalizesProgramFilesAndRelaunchesElevatedForProtectedInstallRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(repositoryRoot, "tools", "installer", "AutorecordInstaller.cs"));

        Assert.Contains("NormalizeInstallRoot", source, StringComparison.Ordinal);
        Assert.Contains("Path.Combine(programFiles, \"Autorecord\")", source, StringComparison.Ordinal);
        Assert.Contains("RequiresElevation", source, StringComparison.Ordinal);
        Assert.Contains("Verb = \"runas\"", source, StringComparison.Ordinal);
        Assert.Contains("AssertInstallRootIsSafe", source, StringComparison.Ordinal);
        Assert.Contains("NormalizeInstallPathBox", source, StringComparison.Ordinal);
        Assert.Contains("_installPathBox.Leave += delegate { NormalizeInstallPathBox(showWarning: false); };", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallerBuildsAsWindowsApplicationWithoutConsole()
    {
        var repositoryRoot = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(repositoryRoot, "scripts", "package-installer.ps1"));

        Assert.Contains("/target:winexe", script, StringComparison.Ordinal);
        Assert.DoesNotContain("/target:exe", script, StringComparison.Ordinal);
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
