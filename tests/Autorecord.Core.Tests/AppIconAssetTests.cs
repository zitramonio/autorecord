using System.Drawing;
using System.Xml.Linq;

namespace Autorecord.Core.Tests;

public sealed class AppIconAssetTests
{
    [Fact]
    public void ApplicationIconAssetIsConfiguredAndLoadable()
    {
        var repositoryRoot = FindRepositoryRoot();
        var iconPath = Path.Combine(repositoryRoot, "src", "Autorecord.App", "Assets", "AppIcon.ico");
        var projectPath = Path.Combine(repositoryRoot, "src", "Autorecord.App", "Autorecord.App.csproj");
        var project = XDocument.Load(projectPath);

        Assert.True(File.Exists(iconPath), $"Missing app icon: {iconPath}");
        using var icon = new Icon(iconPath);

        Assert.True(icon.Width > 0);
        Assert.True(icon.Height > 0);
        Assert.Contains(
            project.Descendants("ApplicationIcon"),
            node => string.Equals(node.Value, @"Assets\AppIcon.ico", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TrayIconHostUsesPackagedApplicationIcon()
    {
        var repositoryRoot = FindRepositoryRoot();
        var trayIconHostPath = Path.Combine(repositoryRoot, "src", "Autorecord.App", "Tray", "TrayIconHost.cs");
        var source = File.ReadAllText(trayIconHostPath);

        Assert.Contains("AppIconProvider.LoadTrayIcon()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Drawing.SystemIcons.Application", source, StringComparison.Ordinal);
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
