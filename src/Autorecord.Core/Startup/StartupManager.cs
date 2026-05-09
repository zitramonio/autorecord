using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

[assembly: InternalsVisibleTo("Autorecord.Core.Tests")]

namespace Autorecord.Core.Startup;

public sealed class StartupManager
{
    private const string TaskName = "Autorecord";
    private const int HResultAccessDenied = unchecked((int)0x80070005);
    private readonly IStartupRegistration _taskSchedulerRegistration;
    private readonly IStartupRegistration _fallbackRegistration;

    public StartupManager()
        : this(
            new TaskSchedulerStartupRegistration(TaskName),
            new RegistryRunStartupRegistration(TaskName))
    {
    }

    internal StartupManager(
        IStartupRegistration taskSchedulerRegistration,
        IStartupRegistration fallbackRegistration)
    {
        _taskSchedulerRegistration = taskSchedulerRegistration;
        _fallbackRegistration = fallbackRegistration;
    }

    public bool IsEnabled()
    {
        try
        {
            return _taskSchedulerRegistration.IsEnabled() || _fallbackRegistration.IsEnabled();
        }
        catch (Exception ex) when (IsAccessDenied(ex))
        {
            return _fallbackRegistration.IsEnabled();
        }
    }

    public void SetEnabled(bool enabled, string executablePath)
    {
        if (!enabled)
        {
            DisableAll();
            return;
        }

        ValidateExecutablePath(executablePath);

        try
        {
            _taskSchedulerRegistration.Enable(executablePath);
        }
        catch (Exception ex) when (IsAccessDenied(ex))
        {
            _fallbackRegistration.Enable(executablePath);
            return;
        }

        _fallbackRegistration.Disable();
    }

    internal static void ValidateExecutablePath(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        if (!Path.IsPathFullyQualified(executablePath) || !File.Exists(executablePath))
        {
            throw new ArgumentException("Executable path must be an existing absolute file path.", nameof(executablePath));
        }
    }

    private void DisableAll()
    {
        Exception? firstException = null;
        try
        {
            _taskSchedulerRegistration.Disable();
        }
        catch (Exception ex)
        {
            firstException = ex;
        }

        try
        {
            _fallbackRegistration.Disable();
        }
        catch (Exception ex) when (firstException is not null)
        {
            firstException = new AggregateException(firstException, ex);
        }

        if (firstException is not null)
        {
            ExceptionDispatchInfo.Capture(firstException).Throw();
        }
    }

    private static bool IsAccessDenied(Exception exception)
    {
        return exception is UnauthorizedAccessException
            || exception is COMException { HResult: HResultAccessDenied };
    }
}

internal interface IStartupRegistration
{
    bool IsEnabled();

    void Enable(string executablePath);

    void Disable();
}

internal sealed class TaskSchedulerStartupRegistration(string taskName) : IStartupRegistration
{
    public bool IsEnabled()
    {
        using var service = new TaskService();
        using var task = service.FindTask(taskName);
        return task is not null;
    }

    public void Enable(string executablePath)
    {
        using var service = new TaskService();
        using var definition = service.NewTask();
        definition.RegistrationInfo.Description = "Start Autorecord when the user signs in.";
        definition.Triggers.Add(new LogonTrigger());
        definition.Actions.Add(new ExecAction(executablePath, "--minimized", Path.GetDirectoryName(executablePath)));
        definition.Settings.DisallowStartIfOnBatteries = false;
        definition.Settings.StopIfGoingOnBatteries = false;
        using var registeredTask = service.RootFolder.RegisterTaskDefinition(taskName, definition);
    }

    public void Disable()
    {
        using var service = new TaskService();
        service.RootFolder.DeleteTask(taskName, false);
    }
}

internal sealed class RegistryRunStartupRegistration(string valueName) : IStartupRegistration
{
    private const string RunSubKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunSubKeyPath, writable: false);
        return key?.GetValue(valueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public void Enable(string executablePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunSubKeyPath, writable: true)
            ?? throw new InvalidOperationException("Could not open the current user startup registry key.");
        key.SetValue(valueName, FormatRunCommand(executablePath), RegistryValueKind.String);
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunSubKeyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }

    internal static string FormatRunCommand(string executablePath)
    {
        return $"\"{executablePath}\" --minimized";
    }
}
