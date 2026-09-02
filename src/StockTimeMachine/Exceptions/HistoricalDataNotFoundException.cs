namespace StockTimeMachine.Exceptions;

public class HistoricalDataNotFoundException : Exception
{
    public HistoricalDataNotFoundException(string message) : base(message) { }
}
