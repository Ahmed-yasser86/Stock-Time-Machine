namespace StockTimeMachine.Entities;

public record HistoricalDate(DateOnly Date)
{
    public static HistoricalDate Create(DateOnly date, DateOnly? referenceDate = null)
    {
        var today = referenceDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        if (date > today)
            throw new ArgumentException("Cannot create historical date in the future");
        return new HistoricalDate(date);
    }
}
