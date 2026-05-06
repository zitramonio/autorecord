using Autorecord.Core.Settings;
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
            if (settings.RecordingMode == RecordingMode.TaggedEvents &&
                !title.Contains(settings.EventTag, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return new CalendarEvent(
                title,
                new DateTimeOffset(startsAt.AsUtc),
                new DateTimeOffset(endsAt.AsUtc));
        }
    }
}
