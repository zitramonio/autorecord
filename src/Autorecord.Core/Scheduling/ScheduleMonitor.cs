using Autorecord.Core.Calendar;

namespace Autorecord.Core.Scheduling;

public static class ScheduleMonitor
{
    private static readonly TimeSpan StartLeadTime = TimeSpan.FromSeconds(3);

    public static CalendarEvent? FindDueEvent(
        IEnumerable<CalendarEvent> events,
        DateTimeOffset now,
        bool recordingActive,
        DateTimeOffset? appStartedAt = null,
        IReadOnlySet<DateTimeOffset>? handledStartsAt = null)
    {
        if (recordingActive)
        {
            return null;
        }

        var startBoundary = appStartedAt ?? DateTimeOffset.MinValue;
        return events
            .Where(e => e.StartsAt >= startBoundary)
            .Where(e => handledStartsAt is null || !handledStartsAt.Contains(e.StartsAt))
            .Where(e => e.StartsAt <= now.Add(StartLeadTime) && e.StartsAt >= now.AddMinutes(-1))
            .OrderBy(e => e.StartsAt)
            .FirstOrDefault();
    }
}
