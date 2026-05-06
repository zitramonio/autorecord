# Autorecord MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Windows-only WPF app that reads Yandex Calendar events from an iCal URL and records microphone plus system output into one WAV file during meetings.

**Architecture:** Keep business logic in a testable core library and keep WPF as a thin host. Use small services for settings, calendar sync, scheduling, recording lifecycle, silence confirmation, notifications, tray hosting, and Task Scheduler startup registration.

**Tech Stack:** .NET 10 Windows Desktop, WPF, NAudio 2.3.0, Ical.Net 5.2.1, TaskScheduler 2.12.2, xUnit.

---

## Preflight

The current folder is not a git repository and `dotnet` is not available in `PATH`.

- Install .NET 10 SDK for Windows before implementation. Microsoft lists .NET 10 as an LTS release supported until November 2028: https://learn.microsoft.com/en-us/dotnet/core/releases-and-support
- Initialize git before the first code task so checkpoints are possible.
- Use Windows for implementation and manual audio verification.

## File Structure

- Create: `Autorecord.sln`
- Create: `src/Autorecord.App/Autorecord.App.csproj`
- Create: `src/Autorecord.App/App.xaml`
- Create: `src/Autorecord.App/App.xaml.cs`
- Create: `src/Autorecord.App/MainWindow.xaml`
- Create: `src/Autorecord.App/MainWindow.xaml.cs`
- Create: `src/Autorecord.App/Tray/TrayIconHost.cs`
- Create: `src/Autorecord.App/Notifications/WpfNotificationService.cs`
- Create: `src/Autorecord.App/Dialogs/StopRecordingDialog.xaml`
- Create: `src/Autorecord.App/Dialogs/StopRecordingDialog.xaml.cs`
- Create: `src/Autorecord.Core/Autorecord.Core.csproj`
- Create: `src/Autorecord.Core/Settings/AppSettings.cs`
- Create: `src/Autorecord.Core/Settings/SettingsStore.cs`
- Create: `src/Autorecord.Core/Calendar/CalendarEvent.cs`
- Create: `src/Autorecord.Core/Calendar/CalendarSyncService.cs`
- Create: `src/Autorecord.Core/Scheduling/ScheduleMonitor.cs`
- Create: `src/Autorecord.Core/Recording/RecordingSession.cs`
- Create: `src/Autorecord.Core/Recording/RecordingCoordinator.cs`
- Create: `src/Autorecord.Core/Recording/StopConfirmationPolicy.cs`
- Create: `src/Autorecord.Core/Audio/AudioLevel.cs`
- Create: `src/Autorecord.Core/Audio/IAudioRecorder.cs`
- Create: `src/Autorecord.Core/Audio/NaudioWavRecorder.cs`
- Create: `src/Autorecord.Core/Startup/StartupManager.cs`
- Create: `src/Autorecord.Core/Utilities/RecordingFileNamer.cs`
- Create: `tests/Autorecord.Core.Tests/Autorecord.Core.Tests.csproj`
- Create: `tests/Autorecord.Core.Tests/CalendarSyncServiceTests.cs`
- Create: `tests/Autorecord.Core.Tests/ScheduleMonitorTests.cs`
- Create: `tests/Autorecord.Core.Tests/RecordingFileNamerTests.cs`
- Create: `tests/Autorecord.Core.Tests/StopConfirmationPolicyTests.cs`
- Create: `tests/Autorecord.Core.Tests/SettingsStoreTests.cs`

## Task 1: Repository and Solution Skeleton

**Files:**
- Create: `Autorecord.sln`
- Create: `src/Autorecord.App/Autorecord.App.csproj`
- Create: `src/Autorecord.Core/Autorecord.Core.csproj`
- Create: `tests/Autorecord.Core.Tests/Autorecord.Core.Tests.csproj`

- [ ] **Step 1: Initialize git if needed**

Run:

```powershell
git status
```

Expected if not initialized: `fatal: not a git repository`.

Run:

```powershell
git init
```

Expected: repository initialized in `C:\Projects\autorecord\.git`.

- [ ] **Step 2: Verify .NET SDK**

Run:

```powershell
dotnet --info
```

Expected: SDK version `10.x` is shown. If not, install .NET 10 SDK and reopen the terminal.

- [ ] **Step 3: Create solution and projects**

Run:

```powershell
dotnet new sln -n Autorecord
dotnet new classlib -n Autorecord.Core -o src/Autorecord.Core -f net10.0-windows
dotnet new wpf -n Autorecord.App -o src/Autorecord.App -f net10.0-windows
dotnet new xunit -n Autorecord.Core.Tests -o tests/Autorecord.Core.Tests -f net10.0-windows
dotnet sln Autorecord.sln add src/Autorecord.Core/Autorecord.Core.csproj
dotnet sln Autorecord.sln add src/Autorecord.App/Autorecord.App.csproj
dotnet sln Autorecord.sln add tests/Autorecord.Core.Tests/Autorecord.Core.Tests.csproj
dotnet add src/Autorecord.App/Autorecord.App.csproj reference src/Autorecord.Core/Autorecord.Core.csproj
dotnet add tests/Autorecord.Core.Tests/Autorecord.Core.Tests.csproj reference src/Autorecord.Core/Autorecord.Core.csproj
```

Expected: all commands succeed.

- [ ] **Step 4: Add packages**

Run:

```powershell
dotnet add src/Autorecord.Core/Autorecord.Core.csproj package Ical.Net --version 5.2.1
dotnet add src/Autorecord.Core/Autorecord.Core.csproj package NAudio --version 2.3.0
dotnet add src/Autorecord.Core/Autorecord.Core.csproj package TaskScheduler --version 2.12.2
```

Expected: NuGet restore succeeds.

- [ ] **Step 5: Enable WinForms interop for tray icon**

Modify `src/Autorecord.App/Autorecord.App.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Autorecord.Core\Autorecord.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 6: Build**

Run:

```powershell
dotnet build Autorecord.sln
```

Expected: `Build succeeded`.

- [ ] **Step 7: Commit**

Run:

```powershell
git add Autorecord.sln src tests
git commit -m "chore: create autorecord solution"
```

Expected: commit created.

## Task 2: Settings and File Naming

**Files:**
- Create: `src/Autorecord.Core/Settings/AppSettings.cs`
- Create: `src/Autorecord.Core/Settings/SettingsStore.cs`
- Create: `src/Autorecord.Core/Utilities/RecordingFileNamer.cs`
- Test: `tests/Autorecord.Core.Tests/RecordingFileNamerTests.cs`
- Test: `tests/Autorecord.Core.Tests/SettingsStoreTests.cs`

- [ ] **Step 1: Write failing file naming tests**

Create `tests/Autorecord.Core.Tests/RecordingFileNamerTests.cs`:

```csharp
using Autorecord.Core.Utilities;

namespace Autorecord.Core.Tests;

public sealed class RecordingFileNamerTests
{
    [Fact]
    public void BuildsNameFromRecordingStartTime()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        var result = RecordingFileNamer.GetAvailablePath(dir, new DateTimeOffset(2026, 5, 6, 18, 42, 0, TimeSpan.Zero));

        Assert.Equal(Path.Combine(dir, "06.05.2026 18.42.wav"), result);
    }

    [Fact]
    public void AddsSuffixWhenFileAlreadyExists()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "06.05.2026 18.42.wav"), "");

        var result = RecordingFileNamer.GetAvailablePath(dir, new DateTimeOffset(2026, 5, 6, 18, 42, 0, TimeSpan.Zero));

        Assert.Equal(Path.Combine(dir, "06.05.2026 18.42 (2).wav"), result);
    }
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```powershell
dotnet test tests/Autorecord.Core.Tests/Autorecord.Core.Tests.csproj --filter RecordingFileNamerTests
```

Expected: fail because `RecordingFileNamer` does not exist.

- [ ] **Step 3: Implement settings and file namer**

Create `src/Autorecord.Core/Settings/AppSettings.cs`:

```csharp
namespace Autorecord.Core.Settings;

public enum RecordingMode
{
    AllEvents = 0,
    TaggedEvents = 1
}

public sealed record AppSettings
{
    public string CalendarUrl { get; init; } = "";
    public string OutputFolder { get; init; } = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    public RecordingMode RecordingMode { get; init; } = RecordingMode.AllEvents;
    public string EventTag { get; init; } = "record";
    public int SilencePromptMinutes { get; init; } = 1;
    public int RetryPromptMinutes { get; init; } = 5;
    public bool StartWithWindows { get; init; }
}
```

Create `src/Autorecord.Core/Utilities/RecordingFileNamer.cs`:

```csharp
namespace Autorecord.Core.Utilities;

public static class RecordingFileNamer
{
    public static string GetAvailablePath(string outputFolder, DateTimeOffset startedAt)
    {
        Directory.CreateDirectory(outputFolder);
        var baseName = startedAt.LocalDateTime.ToString("dd.MM.yyyy HH.mm");
        var path = Path.Combine(outputFolder, baseName + ".wav");
        if (!File.Exists(path))
        {
            return path;
        }

        for (var index = 2; ; index++)
        {
            var candidate = Path.Combine(outputFolder, $"{baseName} ({index}).wav");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }
}
```

- [ ] **Step 4: Add settings store tests**

Create `tests/Autorecord.Core.Tests/SettingsStoreTests.cs`:

```csharp
using Autorecord.Core.Settings;

namespace Autorecord.Core.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public async Task SaveAndLoadRoundTripsSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        var store = new SettingsStore(path);
        var settings = new AppSettings
        {
            CalendarUrl = "https://example.com/calendar.ics",
            OutputFolder = "C:\\Records",
            RecordingMode = RecordingMode.TaggedEvents,
            EventTag = "запись",
            SilencePromptMinutes = 2,
            RetryPromptMinutes = 10,
            StartWithWindows = true
        };

        await store.SaveAsync(settings, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(settings, loaded);
    }

    [Fact]
    public async Task LoadReturnsDefaultsWhenFileDoesNotExist()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        var store = new SettingsStore(path);

        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(1, loaded.SilencePromptMinutes);
        Assert.Equal(5, loaded.RetryPromptMinutes);
        Assert.Equal(RecordingMode.AllEvents, loaded.RecordingMode);
    }
}
```

- [ ] **Step 5: Implement settings store**

Create `src/Autorecord.Core/Settings/SettingsStore.cs`:

```csharp
using System.Text.Json;

namespace Autorecord.Core.Settings;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public SettingsStore(string path)
    {
        _path = path;
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return new AppSettings();
        }

        await using var stream = File.OpenRead(_path);
        var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken);
        return settings ?? new AppSettings();
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        Validate(settings);
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
    }

    private static void Validate(AppSettings settings)
    {
        if (settings.SilencePromptMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Silence prompt interval must be positive.");
        }

        if (settings.RetryPromptMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Retry prompt interval must be positive.");
        }
    }
}
```

- [ ] **Step 6: Run tests and commit**

Run:

```powershell
dotnet test tests/Autorecord.Core.Tests/Autorecord.Core.Tests.csproj
git add src/Autorecord.Core tests/Autorecord.Core.Tests
git commit -m "feat: add settings and recording file naming"
```

Expected: tests pass and commit is created.

## Task 3: Calendar Parsing and Filtering

**Files:**
- Create: `src/Autorecord.Core/Calendar/CalendarEvent.cs`
- Create: `src/Autorecord.Core/Calendar/CalendarSyncService.cs`
- Test: `tests/Autorecord.Core.Tests/CalendarSyncServiceTests.cs`

- [ ] **Step 1: Write failing calendar tests**

Create `tests/Autorecord.Core.Tests/CalendarSyncServiceTests.cs`:

```csharp
using Autorecord.Core.Calendar;
using Autorecord.Core.Settings;

namespace Autorecord.Core.Tests;

public sealed class CalendarSyncServiceTests
{
    [Fact]
    public void ParseIgnoresAllDayEvents()
    {
        var ics = """
BEGIN:VCALENDAR
BEGIN:VEVENT
SUMMARY:All day
DTSTART;VALUE=DATE:20260506
DTEND;VALUE=DATE:20260507
END:VEVENT
END:VCALENDAR
""";

        var events = CalendarSyncService.ParseEvents(ics, new AppSettings());

        Assert.Empty(events);
    }

    [Fact]
    public void ParseReturnsTimedEvents()
    {
        var ics = """
BEGIN:VCALENDAR
BEGIN:VEVENT
SUMMARY:Demo call
DTSTART:20260506T150000Z
DTEND:20260506T160000Z
END:VEVENT
END:VCALENDAR
""";

        var events = CalendarSyncService.ParseEvents(ics, new AppSettings()).ToList();

        Assert.Single(events);
        Assert.Equal("Demo call", events[0].Title);
        Assert.Equal(new DateTimeOffset(2026, 5, 6, 15, 0, 0, TimeSpan.Zero), events[0].StartsAt);
    }

    [Fact]
    public void TaggedModeKeepsOnlyEventsWithTagInTitle()
    {
        var ics = """
BEGIN:VCALENDAR
BEGIN:VEVENT
SUMMARY:Planning
DTSTART:20260506T150000Z
DTEND:20260506T160000Z
END:VEVENT
BEGIN:VEVENT
SUMMARY:record Interview
DTSTART:20260506T170000Z
DTEND:20260506T180000Z
END:VEVENT
END:VCALENDAR
""";
        var settings = new AppSettings { RecordingMode = RecordingMode.TaggedEvents, EventTag = "record" };

        var events = CalendarSyncService.ParseEvents(ics, settings).ToList();

        Assert.Single(events);
        Assert.Equal("record Interview", events[0].Title);
    }
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```powershell
dotnet test tests/Autorecord.Core.Tests/Autorecord.Core.Tests.csproj --filter CalendarSyncServiceTests
```

Expected: fail because calendar classes do not exist.

- [ ] **Step 3: Implement calendar model and parser**

Create `src/Autorecord.Core/Calendar/CalendarEvent.cs`:

```csharp
namespace Autorecord.Core.Calendar;

public sealed record CalendarEvent(string Title, DateTimeOffset StartsAt, DateTimeOffset EndsAt);
```

Create `src/Autorecord.Core/Calendar/CalendarSyncService.cs`:

```csharp
using Autorecord.Core.Settings;
using Ical.Net;

namespace Autorecord.Core.Calendar;

public sealed class CalendarSyncService
{
    private readonly HttpClient _httpClient;

    public CalendarSyncService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<CalendarEvent>> DownloadAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(settings.CalendarUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        var ics = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseEvents(ics, settings).ToList();
    }

    public static IEnumerable<CalendarEvent> ParseEvents(string ics, AppSettings settings)
    {
        var calendar = Calendar.Load(ics);
        foreach (var item in calendar.Events)
        {
            if (!item.DtStart.HasTime || !item.DtEnd.HasTime)
            {
                continue;
            }

            var title = item.Summary ?? "";
            if (settings.RecordingMode == RecordingMode.TaggedEvents &&
                !title.Contains(settings.EventTag, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return new CalendarEvent(
                title,
                new DateTimeOffset(item.DtStart.AsSystemLocal),
                new DateTimeOffset(item.DtEnd.AsSystemLocal));
        }
    }
}
```

- [ ] **Step 4: Run tests and commit**

Run:

```powershell
dotnet test tests/Autorecord.Core.Tests/Autorecord.Core.Tests.csproj --filter CalendarSyncServiceTests
git add src/Autorecord.Core/Calendar tests/Autorecord.Core.Tests/CalendarSyncServiceTests.cs
git commit -m "feat: parse and filter calendar events"
```

Expected: tests pass and commit is created.

## Task 4: Schedule Monitor

**Files:**
- Create: `src/Autorecord.Core/Scheduling/ScheduleMonitor.cs`
- Test: `tests/Autorecord.Core.Tests/ScheduleMonitorTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/Autorecord.Core.Tests/ScheduleMonitorTests.cs`:

```csharp
using Autorecord.Core.Calendar;
using Autorecord.Core.Scheduling;

namespace Autorecord.Core.Tests;

public sealed class ScheduleMonitorTests
{
    [Fact]
    public void FindsEventStartingAtCurrentMinute()
    {
        var now = new DateTimeOffset(2026, 5, 6, 18, 42, 10, TimeSpan.Zero);
        var events = new[]
        {
            new CalendarEvent("Call", now.AddSeconds(-10), now.AddHours(1))
        };

        var due = ScheduleMonitor.FindDueEvent(events, now, false);

        Assert.NotNull(due);
        Assert.Equal("Call", due.Title);
    }

    [Fact]
    public void DoesNotStartEventThatStartedBeforeApplication()
    {
        var appStartedAt = new DateTimeOffset(2026, 5, 6, 18, 40, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 5, 6, 18, 42, 0, TimeSpan.Zero);
        var events = new[]
        {
            new CalendarEvent("Old", appStartedAt.AddMinutes(-5), now.AddHours(1))
        };

        var due = ScheduleMonitor.FindDueEvent(events, now, false, appStartedAt);

        Assert.Null(due);
    }

    [Fact]
    public void DoesNotStartAnotherEventDuringActiveRecording()
    {
        var now = new DateTimeOffset(2026, 5, 6, 18, 42, 0, TimeSpan.Zero);
        var events = new[]
        {
            new CalendarEvent("Call", now, now.AddHours(1))
        };

        var due = ScheduleMonitor.FindDueEvent(events, now, true);

        Assert.Null(due);
    }
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```powershell
dotnet test tests/Autorecord.Core.Tests/Autorecord.Core.Tests.csproj --filter ScheduleMonitorTests
```

Expected: fail because `ScheduleMonitor` does not exist.

- [ ] **Step 3: Implement schedule monitor**

Create `src/Autorecord.Core/Scheduling/ScheduleMonitor.cs`:

```csharp
using Autorecord.Core.Calendar;

namespace Autorecord.Core.Scheduling;

public static class ScheduleMonitor
{
    public static CalendarEvent? FindDueEvent(
        IEnumerable<CalendarEvent> events,
        DateTimeOffset now,
        bool recordingActive,
        DateTimeOffset? appStartedAt = null)
    {
        if (recordingActive)
        {
            return null;
        }

        var startBoundary = appStartedAt ?? DateTimeOffset.MinValue;
        return events
            .Where(e => e.StartsAt >= startBoundary)
            .Where(e => e.StartsAt <= now && e.StartsAt > now.AddMinutes(-1))
            .OrderBy(e => e.StartsAt)
            .FirstOrDefault();
    }
}
```

- [ ] **Step 4: Run tests and commit**

Run:

```powershell
dotnet test tests/Autorecord.Core.Tests/Autorecord.Core.Tests.csproj --filter ScheduleMonitorTests
git add src/Autorecord.Core/Scheduling tests/Autorecord.Core.Tests/ScheduleMonitorTests.cs
git commit -m "feat: detect due calendar events"
```

Expected: tests pass and commit is created.

## Task 5: Stop Confirmation Policy

**Files:**
- Create: `src/Autorecord.Core/Recording/StopConfirmationPolicy.cs`
- Test: `tests/Autorecord.Core.Tests/StopConfirmationPolicyTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/Autorecord.Core.Tests/StopConfirmationPolicyTests.cs`:

```csharp
using Autorecord.Core.Recording;

namespace Autorecord.Core.Tests;

public sealed class StopConfirmationPolicyTests
{
    [Fact]
    public void PromptsAfterContinuousSilence()
    {
        var policy = new StopConfirmationPolicy(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
        var started = new DateTimeOffset(2026, 5, 6, 18, 0, 0, TimeSpan.Zero);

        Assert.False(policy.ShouldPrompt(started, true));
        Assert.True(policy.ShouldPrompt(started.AddMinutes(1), true));
    }

    [Fact]
    public void NoAnswerKeepsRecordingWithoutChangingRetryWindow()
    {
        var policy = new StopConfirmationPolicy(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
        var promptAt = new DateTimeOffset(2026, 5, 6, 18, 1, 0, TimeSpan.Zero);

        policy.RecordNoAnswer(promptAt);

        Assert.True(policy.ShouldPrompt(promptAt.AddSeconds(1), true));
    }

    [Fact]
    public void NoAnswerAsNoWaitsRetryBeforePromptingAgain()
    {
        var policy = new StopConfirmationPolicy(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
        var promptAt = new DateTimeOffset(2026, 5, 6, 18, 1, 0, TimeSpan.Zero);

        policy.RecordNo(promptAt);

        Assert.False(policy.ShouldPrompt(promptAt.AddMinutes(4), true));
        Assert.True(policy.ShouldPrompt(promptAt.AddMinutes(6), true));
    }

    [Fact]
    public void SoundResetsSilenceTimer()
    {
        var policy = new StopConfirmationPolicy(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
        var now = new DateTimeOffset(2026, 5, 6, 18, 1, 0, TimeSpan.Zero);

        Assert.False(policy.ShouldPrompt(now, false));
    }
}
```

- [ ] **Step 2: Run tests to verify failure**

Run:

```powershell
dotnet test tests/Autorecord.Core.Tests/Autorecord.Core.Tests.csproj --filter StopConfirmationPolicyTests
```

Expected: fail because `StopConfirmationPolicy` does not exist.

- [ ] **Step 3: Implement policy**

Create `src/Autorecord.Core/Recording/StopConfirmationPolicy.cs`:

```csharp
namespace Autorecord.Core.Recording;

public sealed class StopConfirmationPolicy
{
    private readonly TimeSpan _silenceInterval;
    private readonly TimeSpan _retryInterval;
    private DateTimeOffset? _silenceStartedAt;
    private DateTimeOffset? _snoozedUntil;

    public StopConfirmationPolicy(TimeSpan silenceInterval, TimeSpan retryInterval)
    {
        if (silenceInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(silenceInterval));
        if (retryInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(retryInterval));
        _silenceInterval = silenceInterval;
        _retryInterval = retryInterval;
    }

    public bool ShouldPrompt(DateTimeOffset now, bool bothSourcesSilent)
    {
        if (!bothSourcesSilent)
        {
            _silenceStartedAt = null;
            return false;
        }

        _silenceStartedAt ??= now;

        if (_snoozedUntil is not null && now < _snoozedUntil.Value)
        {
            return false;
        }

        return now - _silenceStartedAt.Value >= _silenceInterval;
    }

    public void RecordNo(DateTimeOffset now)
    {
        _snoozedUntil = now + _retryInterval;
        _silenceStartedAt = null;
    }

    public void RecordNoAnswer(DateTimeOffset now)
    {
        _snoozedUntil = null;
    }
}
```

- [ ] **Step 4: Run tests and commit**

Run:

```powershell
dotnet test tests/Autorecord.Core.Tests/Autorecord.Core.Tests.csproj --filter StopConfirmationPolicyTests
git add src/Autorecord.Core/Recording/StopConfirmationPolicy.cs tests/Autorecord.Core.Tests/StopConfirmationPolicyTests.cs
git commit -m "feat: add stop confirmation policy"
```

Expected: tests pass and commit is created.

## Task 6: Recording Interfaces and NAudio WAV Recorder

**Files:**
- Create: `src/Autorecord.Core/Audio/AudioLevel.cs`
- Create: `src/Autorecord.Core/Audio/IAudioRecorder.cs`
- Create: `src/Autorecord.Core/Audio/NaudioWavRecorder.cs`

- [ ] **Step 1: Add audio contracts**

Create `src/Autorecord.Core/Audio/AudioLevel.cs`:

```csharp
namespace Autorecord.Core.Audio;

public sealed record AudioLevel(float InputPeak, float OutputPeak)
{
    public bool BothSilent(float threshold) => InputPeak < threshold && OutputPeak < threshold;
}
```

Create `src/Autorecord.Core/Audio/IAudioRecorder.cs`:

```csharp
namespace Autorecord.Core.Audio;

public interface IAudioRecorder : IAsyncDisposable
{
    event EventHandler<AudioLevel>? LevelChanged;
    Task StartAsync(string outputPath, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Implement minimal NAudio recorder**

Create `src/Autorecord.Core/Audio/NaudioWavRecorder.cs`:

```csharp
using NAudio.Wave;

namespace Autorecord.Core.Audio;

public sealed class NaudioWavRecorder : IAudioRecorder
{
    private WasapiCapture? _input;
    private WasapiLoopbackCapture? _output;
    private WaveFileWriter? _writer;
    private readonly object _gate = new();
    private float _lastInputPeak;
    private float _lastOutputPeak;

    public event EventHandler<AudioLevel>? LevelChanged;

    public Task StartAsync(string outputPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        _input = new WasapiCapture();
        _output = new WasapiLoopbackCapture();
        _writer = new WaveFileWriter(outputPath, _output.WaveFormat);

        _input.DataAvailable += (_, e) =>
        {
            _lastInputPeak = CalculatePeak(e.Buffer, e.BytesRecorded);
            LevelChanged?.Invoke(this, new AudioLevel(_lastInputPeak, _lastOutputPeak));
        };

        _output.DataAvailable += (_, e) =>
        {
            lock (_gate)
            {
                _writer?.Write(e.Buffer, 0, e.BytesRecorded);
            }

            _lastOutputPeak = CalculatePeak(e.Buffer, e.BytesRecorded);
            LevelChanged?.Invoke(this, new AudioLevel(_lastInputPeak, _lastOutputPeak));
        };

        _input.StartRecording();
        _output.StartRecording();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _input?.StopRecording();
        _output?.StopRecording();
        _writer?.Dispose();
        _writer = null;
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _input?.Dispose();
        _output?.Dispose();
    }

    private static float CalculatePeak(byte[] buffer, int bytesRecorded)
    {
        var max = 0f;
        for (var index = 0; index + 1 < bytesRecorded; index += 2)
        {
            var sample = BitConverter.ToInt16(buffer, index) / 32768f;
            max = Math.Max(max, Math.Abs(sample));
        }

        return max;
    }
}
```

- [ ] **Step 3: Build and commit**

Run:

```powershell
dotnet build Autorecord.sln
git add src/Autorecord.Core/Audio
git commit -m "feat: add audio recorder contract"
```

Expected: build passes and commit is created.

## Task 7: Recording Coordinator

**Files:**
- Create: `src/Autorecord.Core/Recording/RecordingSession.cs`
- Create: `src/Autorecord.Core/Recording/RecordingCoordinator.cs`

- [ ] **Step 1: Implement recording session model**

Create `src/Autorecord.Core/Recording/RecordingSession.cs`:

```csharp
using Autorecord.Core.Calendar;

namespace Autorecord.Core.Recording;

public sealed record RecordingSession(CalendarEvent CalendarEvent, DateTimeOffset StartedAt, string OutputPath);
```

- [ ] **Step 2: Implement coordinator**

Create `src/Autorecord.Core/Recording/RecordingCoordinator.cs`:

```csharp
using Autorecord.Core.Audio;
using Autorecord.Core.Calendar;
using Autorecord.Core.Settings;
using Autorecord.Core.Utilities;

namespace Autorecord.Core.Recording;

public sealed class RecordingCoordinator
{
    private const float SilenceThreshold = 0.01f;
    private readonly Func<IAudioRecorder> _recorderFactory;
    private readonly Func<DateTimeOffset> _clock;
    private IAudioRecorder? _recorder;
    private StopConfirmationPolicy? _policy;

    public RecordingSession? CurrentSession { get; private set; }
    public bool IsRecording => CurrentSession is not null;

    public event EventHandler<RecordingSession>? RecordingStarted;
    public event EventHandler<RecordingSession>? StopPromptRequired;
    public event EventHandler<RecordingSession>? RecordingSaved;

    public RecordingCoordinator(Func<IAudioRecorder> recorderFactory, Func<DateTimeOffset> clock)
    {
        _recorderFactory = recorderFactory;
        _clock = clock;
    }

    public async Task StartAsync(CalendarEvent calendarEvent, AppSettings settings, CancellationToken cancellationToken)
    {
        if (IsRecording)
        {
            return;
        }

        var startedAt = _clock();
        var path = RecordingFileNamer.GetAvailablePath(settings.OutputFolder, startedAt);
        _policy = new StopConfirmationPolicy(
            TimeSpan.FromMinutes(settings.SilencePromptMinutes),
            TimeSpan.FromMinutes(settings.RetryPromptMinutes));
        _recorder = _recorderFactory();
        _recorder.LevelChanged += OnLevelChanged;
        CurrentSession = new RecordingSession(calendarEvent, startedAt, path);
        await _recorder.StartAsync(path, cancellationToken);
        RecordingStarted?.Invoke(this, CurrentSession);
    }

    public async Task ConfirmStopAsync(CancellationToken cancellationToken)
    {
        if (_recorder is null || CurrentSession is null)
        {
            return;
        }

        var session = CurrentSession;
        await _recorder.StopAsync(cancellationToken);
        await _recorder.DisposeAsync();
        _recorder = null;
        CurrentSession = null;
        RecordingSaved?.Invoke(this, session);
    }

    public void DeclineStop()
    {
        _policy?.RecordNo(_clock());
    }

    public void IgnoreStopPrompt()
    {
        _policy?.RecordNoAnswer(_clock());
    }

    private void OnLevelChanged(object? sender, AudioLevel level)
    {
        if (CurrentSession is null || _policy is null)
        {
            return;
        }

        if (_policy.ShouldPrompt(_clock(), level.BothSilent(SilenceThreshold)))
        {
            StopPromptRequired?.Invoke(this, CurrentSession);
        }
    }
}
```

- [ ] **Step 3: Build and commit**

Run:

```powershell
dotnet build Autorecord.sln
git add src/Autorecord.Core/Recording
git commit -m "feat: coordinate recording lifecycle"
```

Expected: build passes and commit is created.

## Task 8: Startup Manager

**Files:**
- Create: `src/Autorecord.Core/Startup/StartupManager.cs`

- [ ] **Step 1: Implement Task Scheduler registration**

Create `src/Autorecord.Core/Startup/StartupManager.cs`:

```csharp
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

        var definition = service.NewTask();
        definition.RegistrationInfo.Description = "Start Autorecord when the user signs in.";
        definition.Triggers.Add(new LogonTrigger());
        definition.Actions.Add(new ExecAction(executablePath, "--minimized", Path.GetDirectoryName(executablePath)));
        definition.Settings.DisallowStartIfOnBatteries = false;
        definition.Settings.StopIfGoingOnBatteries = false;
        service.RootFolder.RegisterTaskDefinition(TaskName, definition);
    }
}
```

- [ ] **Step 2: Build and commit**

Run:

```powershell
dotnet build Autorecord.sln
git add src/Autorecord.Core/Startup/StartupManager.cs
git commit -m "feat: manage windows startup task"
```

Expected: build passes and commit is created.

## Task 9: WPF Shell, Tray, Notifications, and Stop Dialog

**Files:**
- Modify: `src/Autorecord.App/App.xaml`
- Modify: `src/Autorecord.App/App.xaml.cs`
- Modify: `src/Autorecord.App/MainWindow.xaml`
- Modify: `src/Autorecord.App/MainWindow.xaml.cs`
- Create: `src/Autorecord.App/Tray/TrayIconHost.cs`
- Create: `src/Autorecord.App/Notifications/WpfNotificationService.cs`
- Create: `src/Autorecord.App/Dialogs/StopRecordingDialog.xaml`
- Create: `src/Autorecord.App/Dialogs/StopRecordingDialog.xaml.cs`

- [ ] **Step 1: Implement notification service**

Create `src/Autorecord.App/Notifications/WpfNotificationService.cs`:

```csharp
using System.Windows;

namespace Autorecord.App.Notifications;

public sealed class WpfNotificationService
{
    public void ShowInfo(string title, string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information));
    }
}
```

- [ ] **Step 2: Implement stop dialog**

Create `src/Autorecord.App/Dialogs/StopRecordingDialog.xaml`:

```xml
<Window x:Class="Autorecord.App.Dialogs.StopRecordingDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Остановить запись?"
        Width="360"
        Height="160"
        WindowStartupLocation="CenterScreen"
        Topmost="True"
        ResizeMode="NoResize">
    <Grid Margin="16">
        <Grid.RowDefinitions>
            <RowDefinition />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>
        <TextBlock Text="На вводе и выводе тишина. Остановить запись?"
                   TextWrapping="Wrap"
                   VerticalAlignment="Center" />
        <StackPanel Grid.Row="1" Orientation="Horizontal" HorizontalAlignment="Right">
            <Button Width="88" Margin="0,0,8,0" IsDefault="True" Click="Yes_Click">Да</Button>
            <Button Width="88" IsCancel="True" Click="No_Click">Нет</Button>
        </StackPanel>
    </Grid>
</Window>
```

Create `src/Autorecord.App/Dialogs/StopRecordingDialog.xaml.cs`:

```csharp
using System.Windows;

namespace Autorecord.App.Dialogs;

public partial class StopRecordingDialog : Window
{
    public StopRecordingDialog()
    {
        InitializeComponent();
    }

    private void Yes_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void No_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
```

- [ ] **Step 3: Implement tray host**

Create `src/Autorecord.App/Tray/TrayIconHost.cs`:

```csharp
using System.Windows;
using Forms = System.Windows.Forms;

namespace Autorecord.App.Tray;

public sealed class TrayIconHost : IDisposable
{
    private readonly Window _window;
    private readonly Forms.NotifyIcon _icon;

    public TrayIconHost(Window window)
    {
        _window = window;
        _icon = new Forms.NotifyIcon
        {
            Text = "Autorecord",
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _icon.DoubleClick += (_, _) => ShowWindow();
    }

    public void ShowBalloon(string title, string text)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = text;
        _icon.ShowBalloonTip(5000);
    }

    private Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Открыть", null, (_, _) => ShowWindow());
        menu.Items.Add("Выход", null, (_, _) => Application.Current.Shutdown());
        return menu;
    }

    private void ShowWindow()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public void Dispose()
    {
        _icon.Dispose();
    }
}
```

- [ ] **Step 4: Implement main window layout**

Replace `src/Autorecord.App/MainWindow.xaml`:

```xml
<Window x:Class="Autorecord.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Autorecord"
        Width="720"
        Height="520">
    <Grid Margin="16">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition/>
        </Grid.RowDefinitions>
        <TextBlock Text="iCal-ссылка" />
        <TextBox x:Name="CalendarUrlBox" Grid.Row="1" Margin="0,6,0,12"/>
        <Button Grid.Row="2" Width="180" HorizontalAlignment="Left" Click="RefreshCalendar_Click">Обновить календарь</Button>
        <StackPanel Grid.Row="3" Margin="0,16,0,0">
            <TextBlock Text="Папка сохранения" />
            <DockPanel Margin="0,6,0,12">
                <Button DockPanel.Dock="Right" Width="100" Click="ChooseFolder_Click">Выбрать</Button>
                <TextBox x:Name="OutputFolderBox" Margin="0,0,8,0"/>
            </DockPanel>
            <CheckBox x:Name="TaggedModeBox" Content="Записывать только события с меткой"/>
            <TextBox x:Name="EventTagBox" Margin="0,6,0,12"/>
            <TextBlock Text="Минут тишины до запроса"/>
            <TextBox x:Name="SilenceMinutesBox" Margin="0,6,0,12"/>
            <TextBlock Text="Минут ожидания после ответа Нет"/>
            <TextBox x:Name="RetryMinutesBox" Margin="0,6,0,12"/>
            <CheckBox x:Name="StartupBox" Content="Запускать вместе с Windows"/>
            <Button Width="120" Margin="0,16,0,0" HorizontalAlignment="Left" Click="Save_Click">Сохранить</Button>
        </StackPanel>
        <TextBlock x:Name="StatusText" Grid.Row="5" VerticalAlignment="Bottom"/>
    </Grid>
</Window>
```

- [ ] **Step 5: Wire minimal code-behind**

Replace `src/Autorecord.App/MainWindow.xaml.cs`:

```csharp
using System.Windows;
using Autorecord.Core.Settings;
using Forms = System.Windows.Forms;

namespace Autorecord.App;

public partial class MainWindow : Window
{
    private AppSettings _settings = new();

    public MainWindow()
    {
        InitializeComponent();
        LoadIntoForm(_settings);
    }

    public event EventHandler? RefreshCalendarRequested;
    public event EventHandler<AppSettings>? SettingsSaved;

    private void RefreshCalendar_Click(object sender, RoutedEventArgs e)
    {
        RefreshCalendarRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog();
        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            OutputFolderBox.Text = dialog.SelectedPath;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings = ReadFromForm();
        SettingsSaved?.Invoke(this, _settings);
    }

    public void SetSettings(AppSettings settings)
    {
        _settings = settings;
        LoadIntoForm(settings);
    }

    public void SetStatus(string status)
    {
        StatusText.Text = status;
    }

    private void LoadIntoForm(AppSettings settings)
    {
        CalendarUrlBox.Text = settings.CalendarUrl;
        OutputFolderBox.Text = settings.OutputFolder;
        TaggedModeBox.IsChecked = settings.RecordingMode == RecordingMode.TaggedEvents;
        EventTagBox.Text = settings.EventTag;
        SilenceMinutesBox.Text = settings.SilencePromptMinutes.ToString();
        RetryMinutesBox.Text = settings.RetryPromptMinutes.ToString();
        StartupBox.IsChecked = settings.StartWithWindows;
    }

    private AppSettings ReadFromForm()
    {
        return new AppSettings
        {
            CalendarUrl = CalendarUrlBox.Text.Trim(),
            OutputFolder = OutputFolderBox.Text.Trim(),
            RecordingMode = TaggedModeBox.IsChecked == true ? RecordingMode.TaggedEvents : RecordingMode.AllEvents,
            EventTag = EventTagBox.Text.Trim(),
            SilencePromptMinutes = int.Parse(SilenceMinutesBox.Text.Trim()),
            RetryPromptMinutes = int.Parse(RetryMinutesBox.Text.Trim()),
            StartWithWindows = StartupBox.IsChecked == true
        };
    }
}
```

- [ ] **Step 6: Build and commit**

Run:

```powershell
dotnet build Autorecord.sln
git add src/Autorecord.App
git commit -m "feat: add wpf settings shell"
```

Expected: build passes and commit is created.

## Task 10: Application Host Wiring

**Files:**
- Modify: `src/Autorecord.App/App.xaml`
- Modify: `src/Autorecord.App/App.xaml.cs`

- [ ] **Step 1: Replace App.xaml startup behavior**

Replace `src/Autorecord.App/App.xaml`:

```xml
<Application x:Class="Autorecord.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources />
</Application>
```

- [ ] **Step 2: Wire services in App.xaml.cs**

Replace `src/Autorecord.App/App.xaml.cs`:

```csharp
using System.Windows;
using Autorecord.App.Dialogs;
using Autorecord.App.Tray;
using Autorecord.Core.Audio;
using Autorecord.Core.Calendar;
using Autorecord.Core.Recording;
using Autorecord.Core.Scheduling;
using Autorecord.Core.Settings;
using Autorecord.Core.Startup;

namespace Autorecord.App;

public partial class App : Application
{
    private readonly DateTimeOffset _appStartedAt = DateTimeOffset.Now;
    private readonly SettingsStore _settingsStore = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Autorecord",
        "settings.json"));
    private readonly CalendarSyncService _calendarSync = new(new HttpClient());
    private readonly StartupManager _startupManager = new();
    private RecordingCoordinator? _recording;
    private MainWindow? _window;
    private TrayIconHost? _tray;
    private AppSettings _settings = new();
    private IReadOnlyList<CalendarEvent> _events = Array.Empty<CalendarEvent>();
    private readonly PeriodicTimer _calendarTimer = new(TimeSpan.FromHours(1));
    private readonly PeriodicTimer _scheduleTimer = new(TimeSpan.FromSeconds(15));

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _settings = await _settingsStore.LoadAsync(CancellationToken.None);
        _window = new MainWindow();
        _window.SetSettings(_settings);
        _window.RefreshCalendarRequested += async (_, _) => await RefreshCalendarAsync();
        _window.SettingsSaved += async (_, settings) => await SaveSettingsAsync(settings);
        _tray = new TrayIconHost(_window);
        _recording = new RecordingCoordinator(() => new NaudioWavRecorder(), () => DateTimeOffset.Now);
        _recording.RecordingStarted += (_, session) => _tray.ShowBalloon("Запись началась", session.CalendarEvent.Title);
        _recording.RecordingSaved += (_, session) => _tray.ShowBalloon("Запись сохранена", session.OutputPath);
        _recording.StopPromptRequired += async (_, _) => await AskToStopRecordingAsync();

        if (!e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase))
        {
            _window.Show();
        }

        _ = RunCalendarLoopAsync();
        _ = RunScheduleLoopAsync();
        await RefreshCalendarAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _calendarTimer.Dispose();
        _scheduleTimer.Dispose();
        _tray?.Dispose();
        base.OnExit(e);
    }

    private async Task SaveSettingsAsync(AppSettings settings)
    {
        _settings = settings;
        await _settingsStore.SaveAsync(settings, CancellationToken.None);
        _startupManager.SetEnabled(settings.StartWithWindows, Environment.ProcessPath ?? "");
        _window?.SetStatus("Настройки сохранены");
    }

    private async Task RefreshCalendarAsync()
    {
        try
        {
            _events = await _calendarSync.DownloadAsync(_settings, CancellationToken.None);
            _window?.SetStatus($"Календарь обновлен: {DateTime.Now:dd.MM.yyyy HH:mm}");
        }
        catch (Exception ex)
        {
            _window?.SetStatus("Ошибка календаря: " + ex.Message);
        }
    }

    private async Task RunCalendarLoopAsync()
    {
        while (await _calendarTimer.WaitForNextTickAsync())
        {
            await RefreshCalendarAsync();
        }
    }

    private async Task RunScheduleLoopAsync()
    {
        while (await _scheduleTimer.WaitForNextTickAsync())
        {
            var due = ScheduleMonitor.FindDueEvent(_events, DateTimeOffset.Now, _recording?.IsRecording == true, _appStartedAt);
            if (due is not null && _recording is not null)
            {
                await _recording.StartAsync(due, _settings, CancellationToken.None);
                _window?.SetStatus("Идет запись: " + due.Title);
            }
        }
    }

    private async Task AskToStopRecordingAsync()
    {
        await Dispatcher.InvokeAsync(async () =>
        {
            var dialog = new StopRecordingDialog();
            var result = dialog.ShowDialog();
            if (_recording is null)
            {
                return;
            }

            if (result == true)
            {
                await _recording.ConfirmStopAsync(CancellationToken.None);
                _window?.SetStatus("Запись сохранена");
            }
            else if (result == false)
            {
                _recording.DeclineStop();
            }
            else
            {
                _recording.IgnoreStopPrompt();
            }
        });
    }
}
```

- [ ] **Step 3: Build and commit**

Run:

```powershell
dotnet build Autorecord.sln
git add src/Autorecord.App/App.xaml src/Autorecord.App/App.xaml.cs
git commit -m "feat: wire autorecord app host"
```

Expected: build passes and commit is created.

## Task 11: Verification Pass

**Files:**
- Modify only files needed to fix verification failures.

- [ ] **Step 1: Run full automated checks**

Run:

```powershell
dotnet test Autorecord.sln
dotnet build Autorecord.sln -c Release
```

Expected: tests pass and release build succeeds.

- [ ] **Step 2: Manual calendar check**

Run:

```powershell
dotnet run --project src/Autorecord.App/Autorecord.App.csproj
```

Expected:
- window opens;
- iCal URL can be entered;
- output folder can be selected;
- manual calendar update changes status to `Календарь обновлен`.

- [ ] **Step 3: Manual recording check**

Use a test `.ics` event starting in the next 1-2 minutes or temporarily point the parser to a local test URL during development.

Expected:
- notification appears when recording starts;
- WAV file is created in the selected folder;
- microphone and system output are audible in the saved file;
- after configured silence, stop dialog appears;
- `Нет` keeps recording;
- `Да` saves and closes the session.

- [ ] **Step 4: Manual startup check**

Enable startup in GUI, then run:

```powershell
Get-ScheduledTask -TaskName Autorecord
```

Expected: scheduled task exists.

Sign out and sign in again.

Expected: app starts minimized to tray.

- [ ] **Step 5: Commit verification fixes**

Run:

```powershell
git status --short
git add .
git commit -m "fix: complete mvp verification"
```

Expected: only verification-related fixes are committed.

## Self-Review

- Spec coverage:
  - Windows-only WPF app: Tasks 1, 9, 10.
  - iCal calendar sync: Task 3 and Task 10.
  - Hourly and manual refresh: Task 10.
  - Record default input and output into one WAV: Task 6 and Task 7.
  - File naming `dd.MM.yyyy HH.mm.wav` and suffix: Task 2.
  - Stop prompt after silence and retry after `Нет`: Task 5 and Task 10.
  - GUI settings for folder, URL, mode, intervals, startup: Task 9.
  - Notifications and stop dialog: Task 9 and Task 10.
  - Startup after restart: Task 8 and Task 11.
- Placeholder scan: no placeholder markers or undefined future work is required for the MVP path.
- Type consistency:
  - `AppSettings`, `CalendarEvent`, `RecordingSession`, and `RecordingCoordinator` signatures are reused consistently across tasks.
  - The WPF host references only classes defined in earlier tasks.

## References

- .NET support policy: https://learn.microsoft.com/en-us/dotnet/core/releases-and-support
- WPF documentation: https://learn.microsoft.com/en-us/dotnet/desktop/wpf/
- NAudio package: https://www.nuget.org/packages/NAudio/
- Ical.Net package: https://www.nuget.org/packages/Ical.Net/
- TaskScheduler package: https://www.nuget.org/packages/TaskScheduler/
