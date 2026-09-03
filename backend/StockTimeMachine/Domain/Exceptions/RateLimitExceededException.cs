namespace StockTimeMachine;

public class RateLimitExceededException : Exception
{
    public RateLimitExceededException(string message) : base(message) { }
}
