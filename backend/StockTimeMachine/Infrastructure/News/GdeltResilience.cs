using Microsoft.Extensions.Logging;

namespace StockTimeMachine;

// Smart throttling for the GDELT family: honor the server's Retry-After,
// otherwise exponential backoff with jitter, bounded retries. Anything else
// (timeouts, parse errors, persistent throttling) still degrades to honest
// empty at the provider layer — retries rescue transient pressure, never
// mask real outages.
public static class GdeltResilience
{
    private const int MaxRetries = 3;
    private static readonly TimeSpan[] Backoffs = new[]
    {
        TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(20),
    };
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(60);
    private static readonly Random Jitter = new();

    public static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> fetch, ILogger logger, string operation, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await fetch(ct);
            }
            catch (RateLimitExceededException ex) when (attempt < MaxRetries)
            {
                var asked = ex.RetryAfter;
                var wait = asked ?? Backoffs[Math.Min(attempt, Backoffs.Length - 1)];
                if (wait > MaxWait)
                    wait = MaxWait;
                if (asked is null)
                    wait += TimeSpan.FromMilliseconds(Jitter.Next(0, 1000));
                logger.LogWarning(ex, "{Operation} throttled (attempt {Attempt}); waiting {Wait} before retry",
                    operation, attempt + 1, wait);
                await Task.Delay(wait, ct);
            }
        }
    }

    // Retry-After: delta-seconds ("120") or HTTP date. Null when absent or
    // unparseable — callers fall back to backoff.
    public static TimeSpan? ParseRetryAfter(System.Net.Http.Headers.HttpResponseHeaders headers)
    {
        if (!headers.TryGetValues("Retry-After", out var values))
            return null;
        var raw = values.FirstOrDefault()?.Trim() ?? "";
        if (int.TryParse(raw, out var seconds) && seconds >= 0)
            return TimeSpan.FromSeconds(seconds);
        if (DateTimeOffset.TryParse(raw, out var date))
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }
        return null;
    }
}
