using Microsoft.Win32.TaskScheduler;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Autorecord.Core.Tests")]

namespace Autorecord.Core.Startup;

public sealed class StartupManager
{
    private const string TaskName = "Autorecord";

    public bool IsEnabled()
    {
        using var service = new TaskService();
        using var task = service.FindTask(TaskName);
        return task is not null;
    }

    public void SetEnabled(bool enabled, string executablePath)
    {
        using var service = new TaskService();
        if (!enabled)
        {
            service.RootFolder.DeleteTask(TaskName, false);
            return;
        }

        ValidateExecutablePath(executablePath);

        using var definition = service.NewTask();
        definition.RegistrationInfo.Description = "Start Autorecord when the user signs in.";
        definition.Triggers.Add(new LogonTrigger());
        definition.Actions.Add(new ExecAction(executablePath, "--minimized", Path.GetDirectoryName(executablePath)));
        definition.Settings.DisallowStartIfOnBatteries = false;
        definition.Settings.StopIfGoingOnBatteries = false;
        using var registeredTask = service.RootFolder.RegisterTaskDefinition(TaskName, definition);
    }

    internal static void ValidateExecutablePath(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        if (!Path.IsPathFullyQualified(executablePath) || !File.Exists(executablePath))
        {
            throw new ArgumentException("Executable path must be an existing absolute file path.", nameof(executablePath));
        }
    }
}
