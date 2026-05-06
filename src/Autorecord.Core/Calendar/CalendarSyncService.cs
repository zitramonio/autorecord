using Autorecord.Core.Settings;
using Ical.Net.DataTypes;
using IcalCalendar = Ical.Net.Calendar;

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
        var calendar = IcalCalendar.Load(ics)!;
        foreach (var item in calendar.Events)
        {
            var startsAt = item.DtStart;
            var endsAt = item.DtEnd;
            if (startsAt is null || endsAt is null || !startsAt.HasTime || !endsAt.HasTime)
            {
                continue;
            }

            var title = item.Summary ?? "";
            if (settings.RecordingMode == RecordingMode.TaggedEvents)
            {
                if (string.IsNullOrWhiteSpace(settings.EventTag) ||
                    !title.Contains(settings.EventTag, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            yield return new CalendarEvent(
                title,
                ToDateTimeOffset(startsAt),
                ToDateTimeOffset(endsAt));
        }
    }

    private static DateTimeOffset ToDateTimeOffset(CalDateTime value)
    {
        if (value.IsUtc)
        {
            return new DateTimeOffset(value.AsUtc, TimeSpan.Zero);
        }

        var localTime = DateTime.SpecifyKind(value.Value, DateTimeKind.Unspecified);
        if (!value.IsFloating &&
            !string.IsNullOrWhiteSpace(value.TzId) &&
            TimeZoneInfo.TryFindSystemTimeZoneById(value.TzId, out var timeZone))
        {
            return new DateTimeOffset(localTime, timeZone.GetUtcOffset(localTime));
        }

        return new DateTimeOffset(localTime, TimeZoneInfo.Local.GetUtcOffset(localTime));
    }
}
