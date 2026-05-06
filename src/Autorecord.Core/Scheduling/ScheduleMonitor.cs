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
