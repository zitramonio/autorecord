using Autorecord.Core.Calendar;

namespace Autorecord.Core.Recording;

public sealed record RecordingSession(CalendarEvent CalendarEvent, DateTimeOffset StartedAt, string OutputPath);
