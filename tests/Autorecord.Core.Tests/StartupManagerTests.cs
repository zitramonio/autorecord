using Autorecord.Core.Startup;

namespace Autorecord.Core.Tests;

public sealed class StartupManagerTests
{
    [Fact]
    public void SetEnabledFallsBackWhenTaskSchedulerAccessIsDenied()
    {
        var primary = new FakeStartupRegistration
        {
            EnableException = new UnauthorizedAccessException("Access is denied.")
        };
        var fallback = new FakeStartupRegistration();
        var manager = new StartupManager(primary, fallback);
        using var executable = TemporaryExecutable.Create();

        manager.SetEnabled(true, executable.Path);

        Assert.Equal(1, primary.EnableCalls);
        Assert.Equal(executable.Path, primary.LastExecutablePath);
        Assert.True(fallback.Enabled);
        Assert.Equal(executable.Path, fallback.LastExecutablePath);
    }

    [Fact]
    public void SetEnabledDoesNotFallBackForUnexpectedTaskSchedulerError()
    {
        var primary = new FakeStartupRegistration
        {
            EnableException = new InvalidOperationException("Unexpected failure.")
        };
        var fallback = new FakeStartupRegistration();
        var manager = new StartupManager(primary, fallback);
        using var executable = TemporaryExecutable.Create();

        Assert.Throws<InvalidOperationException>(() => manager.SetEnabled(true, executable.Path));
        Assert.False(fallback.Enabled);
    }

    [Fact]
    public void IsEnabledReturnsTrueWhenFallbackRegistrationExists()
    {
        var primary = new FakeStartupRegistration();
        var fallback = new FakeStartupRegistration { Enabled = true };
        var manager = new StartupManager(primary, fallback);

        Assert.True(manager.IsEnabled());
    }

    [Fact]
    public void IsEnabledFallsBackWhenTaskSchedulerAccessIsDenied()
    {
        var primary = new FakeStartupRegistration
        {
            IsEnabledException = new UnauthorizedAccessException("Access is denied.")
        };
        var fallback = new FakeStartupRegistration { Enabled = true };
        var manager = new StartupManager(primary, fallback);

        Assert.True(manager.IsEnabled());
    }

    [Fact]
    public void SetEnabledFalseDisablesTaskSchedulerAndFallbackRegistrations()
    {
        var primary = new FakeStartupRegistration { Enabled = true };
        var fallback = new FakeStartupRegistration { Enabled = true };
        var manager = new StartupManager(primary, fallback);

        manager.SetEnabled(false, "");

        Assert.False(primary.Enabled);
        Assert.False(fallback.Enabled);
        Assert.Equal(1, primary.DisableCalls);
        Assert.Equal(1, fallback.DisableCalls);
    }

    [Fact]
    public void SetEnabledFalseStillDisablesFallbackWhenTaskSchedulerDisableFails()
    {
        var primary = new FakeStartupRegistration
        {
            Enabled = true,
            DisableException = new UnauthorizedAccessException("Access is denied.")
        };
        var fallback = new FakeStartupRegistration { Enabled = true };
        var manager = new StartupManager(primary, fallback);

        Assert.Throws<UnauthorizedAccessException>(() => manager.SetEnabled(false, ""));
        Assert.False(fallback.Enabled);
        Assert.Equal(1, fallback.DisableCalls);
    }

    [Fact]
    public void SetEnabledFallsBackForTaskSchedulerComAccessDenied()
    {
        var primary = new FakeStartupRegistration
        {
            EnableException = new System.Runtime.InteropServices.COMException(
                "Access is denied.",
                unchecked((int)0x80070005))
        };
        var fallback = new FakeStartupRegistration();
        var manager = new StartupManager(primary, fallback);
        using var executable = TemporaryExecutable.Create();

        manager.SetEnabled(true, executable.Path);

        Assert.True(fallback.Enabled);
    }

    [Fact]
    public void RegistryRunStartupRegistrationFormatsMinimizedCommand()
    {
        var executablePath = @"C:\Program Files\Autorecord\Autorecord.App.exe";

        var command = RegistryRunStartupRegistration.FormatRunCommand(executablePath);

        Assert.Equal("\"C:\\Program Files\\Autorecord\\Autorecord.App.exe\" --minimized", command);
    }

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

    private sealed class FakeStartupRegistration : IStartupRegistration
    {
        public bool Enabled { get; set; }
        public Exception? IsEnabledException { get; init; }
        public Exception? EnableException { get; init; }
        public Exception? DisableException { get; init; }
        public int EnableCalls { get; private set; }
        public int DisableCalls { get; private set; }
        public string? LastExecutablePath { get; private set; }

        public bool IsEnabled()
        {
            if (IsEnabledException is not null)
            {
                throw IsEnabledException;
            }

            return Enabled;
        }

        public void Enable(string executablePath)
        {
            EnableCalls++;
            LastExecutablePath = executablePath;
            if (EnableException is not null)
            {
                throw EnableException;
            }

            Enabled = true;
        }

        public void Disable()
        {
            DisableCalls++;
            if (DisableException is not null)
            {
                throw DisableException;
            }

            Enabled = false;
        }
    }

    private sealed class TemporaryExecutable : IDisposable
    {
        private readonly string _directory;

        private TemporaryExecutable(string directory, string path)
        {
            _directory = directory;
            Path = path;
        }

        public string Path { get; }

        public static TemporaryExecutable Create()
        {
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var executablePath = System.IO.Path.Combine(directory, "Autorecord.App.exe");
            Directory.CreateDirectory(directory);
            File.WriteAllText(executablePath, "");
            return new TemporaryExecutable(directory, executablePath);
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
