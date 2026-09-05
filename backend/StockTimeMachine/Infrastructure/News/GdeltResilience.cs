using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace StockTimeMachine;

// Smart throttling for the GDELT family, driven by the global adaptive
// limiter: honor the server's Retry-After, otherwise the limiter's growing
// backoff; rhythm halves on every 429 and recovers gradually on success.
// Anything else (timeouts, parse errors, exhausted attempts) still degrades
// to honest empty at the provider layer — retries rescue transient pressure,
// never mask real outages.
public static class GdeltResilience
{
    public static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> fetch,
        ILogger logger,
        string operation,
        IConfiguration config,
        CancellationToken ct)
    {
        var limiter = RateLimiterRegistry.Get("gdelt", config);
        int attempts = 0;
        while (true)
        {
            await limiter.AcquireAsync(0, ct);
            try
            {
                var result = await fetch(ct);
                limiter.ReportSuccess();
                return result;
            }
            catch (RateLimitExceededException ex)
            {
                attempts++;
                if (attempts >= limiter.MaxAttempts)
                    throw;
                var pause = limiter.ReportThrottled(ex.RetryAfter);
                logger.LogWarning(ex, "{Operation} throttled (attempt {Attempt}); waiting {Pause} before retry",
                    operation, attempts, pause);
                await Task.Delay(pause, ct);
            }
        }
    }

}
