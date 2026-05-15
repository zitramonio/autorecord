namespace Autorecord.Core.Tests;

public sealed class HuggingFaceTokenDialogTests
{
    [Fact]
    public void DialogContainsStepByStepInstructionsAndTokenFieldAtBottom()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "Autorecord.App",
            "Dialogs",
            "HuggingFaceTokenDialog.xaml"));

        Assert.Contains("Шаг 1", xaml, StringComparison.Ordinal);
        Assert.Contains("https://huggingface.co/login", xaml, StringComparison.Ordinal);
        Assert.Contains("зарегистрируйтесь", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Шаг 2", xaml, StringComparison.Ordinal);
        Assert.Contains("Шаг 3", xaml, StringComparison.Ordinal);
        Assert.Contains("Шаг 4", xaml, StringComparison.Ordinal);
        Assert.Contains("https://huggingface.co/pyannote/speaker-diarization-community-1", xaml, StringComparison.Ordinal);
        Assert.Contains("https://huggingface.co/settings/tokens", xaml, StringComparison.Ordinal);
        Assert.Contains("Hyperlink NavigateUri=\"https://huggingface.co/login\"", xaml, StringComparison.Ordinal);
        Assert.Contains("RequestNavigate=\"OpenLink_RequestNavigate\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Assets/HuggingFace/hf1.png", xaml, StringComparison.Ordinal);
        Assert.Contains("Assets/HuggingFace/hf8.png", xaml, StringComparison.Ordinal);

        var finalStepIndex = xaml.IndexOf("Шаг 4", StringComparison.Ordinal);
        var tokenBoxIndex = xaml.IndexOf("x:Name=\"TokenBox\"", StringComparison.Ordinal);
        Assert.True(tokenBoxIndex > finalStepIndex);
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
