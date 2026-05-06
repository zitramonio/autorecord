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
