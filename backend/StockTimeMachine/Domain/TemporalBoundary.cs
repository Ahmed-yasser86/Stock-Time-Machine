namespace StockTimeMachine;

public static class TemporalBoundary
{
    // US Eastern Time (America/New_York). Windows uses "Eastern Standard Time",
    // Linux/macOS use IANA "America/New_York". Resolved lazily so a missing
    // zone throws a descriptive error at first use, not at process startup.
    private static readonly Lazy<TimeZoneInfo> EasternZone = new(FindEasternZone);

    private static TimeZoneInfo FindEasternZone()
    {
        foreach (var id in new[] { "Eastern Standard Time", "America/New_York" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
        }
        throw new TimeZoneNotFoundException(
            "US Eastern time zone not found. Tried 'Eastern Standard Time' and 'America/New_York'.");
    }

    public static DateTime GetCutoffUtc(DateOnly selectedDate)
    {
        var endOfDayEastern = new DateTime(
            selectedDate.Year, selectedDate.Month, selectedDate.Day,
            23, 59, 59, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(endOfDayEastern, EasternZone.Value);
    }

    // Start (midnight UTC) of the day AFTER the selected date.
    // SEC EDGAR publishes calendar filing dates, not instants; providers store
    // them as midnight UTC of that date. A filing dated D is eligible for an
    // investigation as of D, but a filing dated D+1 (midnight UTC = evening of
    // D in US Eastern) must NOT leak into it. Comparing against the end-of-day
    // Eastern cutoff would admit that next-day filing, so date-only evidence
    // is filtered by calendar day through this bound instead.
    public static DateTime StartOfDayAfterUtc(DateOnly selectedDate)
    {
        var next = selectedDate.AddDays(1);
        return new DateTime(next.Year, next.Month, next.Day, 0, 0, 0, DateTimeKind.Utc);
    }
}
