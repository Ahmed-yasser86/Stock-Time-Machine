namespace StockTimeMachine.Entities;

public static class TemporalBoundary
{
    private static readonly TimeZoneInfo EasternZone =
        TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    public static DateTime GetCutoffUtc(DateOnly selectedDate)
    {
        var endOfDayEastern = new DateTime(
            selectedDate.Year, selectedDate.Month, selectedDate.Day,
            23, 59, 59, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(endOfDayEastern, EasternZone);
    }
}
