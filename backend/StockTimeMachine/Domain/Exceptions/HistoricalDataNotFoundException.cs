namespace StockTimeMachine;

public class HistoricalDataNotFoundException : Exception
{
    public HistoricalDataNotFoundException(string message) : base(message) { }
}
