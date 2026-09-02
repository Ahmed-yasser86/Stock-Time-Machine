namespace StockTimeMachine.Exceptions;

public class InvalidHistoricalDateException : Exception
{
    public InvalidHistoricalDateException(string message) : base(message) { }
}
