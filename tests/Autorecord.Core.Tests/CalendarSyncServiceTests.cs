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

    [Fact]
    public void TaggedModeWithEmptyTagReturnsNoEvents()
    {
        var ics = """
BEGIN:VCALENDAR
BEGIN:VEVENT
SUMMARY:Planning
DTSTART:20260506T150000Z
DTEND:20260506T160000Z
END:VEVENT
END:VCALENDAR
""";
        var settings = new AppSettings { RecordingMode = RecordingMode.TaggedEvents, EventTag = "" };

        var events = CalendarSyncService.ParseEvents(ics, settings).ToList();

        Assert.Empty(events);
    }

    [Fact]
    public void ParseKeepsFloatingTimesAsLocalWallClock()
    {
        var ics = """
BEGIN:VCALENDAR
BEGIN:VEVENT
SUMMARY:Local call
DTSTART:20260506T150000
DTEND:20260506T160000
END:VEVENT
END:VCALENDAR
""";

        var events = CalendarSyncService.ParseEvents(ics, new AppSettings()).ToList();

        Assert.Single(events);
        Assert.Equal(new DateTime(2026, 5, 6, 15, 0, 0), events[0].StartsAt.LocalDateTime);
        Assert.Equal(TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 5, 6, 15, 0, 0)), events[0].StartsAt.Offset);
        Assert.Equal(new DateTime(2026, 5, 6, 16, 0, 0), events[0].EndsAt.LocalDateTime);
    }
}
