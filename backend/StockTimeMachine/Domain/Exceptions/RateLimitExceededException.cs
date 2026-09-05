namespace StockTimeMachine;

public class RateLimitExceededException : Exception
{
    // Server-asked wait before retrying (parsed from Retry-After), if given.
    public TimeSpan? RetryAfter { get; }

    public RateLimitExceededException(string message, TimeSpan? retryAfter = null) : base(message)
    {
        RetryAfter = retryAfter;
    }
}
