
namespace StockTimeMachine;

public record HistoricalDate(DateOnly Date)
{
    public static HistoricalDate Create(DateOnly date, DateOnly? referenceDate = null)
    {
        var today = referenceDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        if (date > today)
            throw new InvalidHistoricalDateException("Please select a date in the past.");
        return new HistoricalDate(date);
    }
}
