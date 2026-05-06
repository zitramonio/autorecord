namespace Autorecord.Core.Calendar;

public sealed record CalendarEvent(string Title, DateTimeOffset StartsAt, DateTimeOffset EndsAt);
