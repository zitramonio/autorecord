using Autorecord.Core.Startup;

namespace Autorecord.Core.Tests;

public sealed class StartupManagerTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ValidateExecutablePathRejectsEmptyOrWhitespacePath(string executablePath)
    {
        Assert.Throws<ArgumentException>(() => StartupManager.ValidateExecutablePath(executablePath));
    }

    [Fact]
    public void ValidateExecutablePathRejectsRelativePath()
    {
        Assert.Throws<ArgumentException>(() => StartupManager.ValidateExecutablePath("Autorecord.App.exe"));
    }

    [Fact]
    public void ValidateExecutablePathRejectsExistingDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            Assert.Throws<ArgumentException>(() => StartupManager.ValidateExecutablePath(directory));
        }
        finally
        {
            Directory.Delete(directory);
        }
    }

    [Fact]
    public void ValidateExecutablePathRejectsMissingAbsoluteFile()
    {
        var executablePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.exe");

        Assert.Throws<ArgumentException>(() => StartupManager.ValidateExecutablePath(executablePath));
    }

    [Fact]
    public void ValidateExecutablePathAcceptsExistingExeFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var executablePath = Path.Combine(directory, "Autorecord.App.exe");
        Directory.CreateDirectory(directory);
        File.WriteAllText(executablePath, "");

        try
        {
            StartupManager.ValidateExecutablePath(executablePath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
