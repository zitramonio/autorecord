using Microsoft.Win32.TaskScheduler;

namespace Autorecord.Core.Startup;

public sealed class StartupManager
{
    private const string TaskName = "Autorecord";

    public bool IsEnabled()
    {
        using var service = new TaskService();
        return service.FindTask(TaskName) is not null;
    }

    public void SetEnabled(bool enabled, string executablePath)
    {
        using var service = new TaskService();
        if (!enabled)
        {
            service.RootFolder.DeleteTask(TaskName, false);
            return;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        var definition = service.NewTask();
        definition.RegistrationInfo.Description = "Start Autorecord when the user signs in.";
        definition.Triggers.Add(new LogonTrigger());
        definition.Actions.Add(new ExecAction(executablePath, "--minimized", Path.GetDirectoryName(executablePath)));
        definition.Settings.DisallowStartIfOnBatteries = false;
        definition.Settings.StopIfGoingOnBatteries = false;
        service.RootFolder.RegisterTaskDefinition(TaskName, definition);
    }
}
