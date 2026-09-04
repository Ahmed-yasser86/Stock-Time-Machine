namespace StockTimeMachine;

// Token-bucket limiter for metered AI calls (Gemini free tier: 30k tokens/min
// on embeddings). Callers WAIT for budget instead of failing: when the bucket
// is empty, WaitAsync delays until enough tokens refill, then proceeds.
// Thread-safe; clock is injectable for tests.
public class TokenBucketRateLimiter
{
    private readonly double _capacity;
    private readonly double _refillPerSecond;
    private readonly Func<DateTime> _clock;
    private readonly object _gate = new();
    private double _tokens;
    private DateTime _last;

    public TokenBucketRateLimiter(double tokensPerMinute, Func<DateTime>? clock = null)
    {
        _capacity = tokensPerMinute;
        _refillPerSecond = tokensPerMinute / 60.0;
        _clock = clock ?? (() => DateTime.UtcNow);
        _tokens = tokensPerMinute;
        _last = _clock();
    }

    public async Task WaitAsync(double tokens, CancellationToken ct = default)
    {
        while (true)
        {
            TimeSpan wait;
            lock (_gate)
            {
                var now = _clock();
                _tokens = Math.Min(_capacity, _tokens + (now - _last).TotalSeconds * _refillPerSecond);
                _last = now;
                if (_tokens >= tokens)
                {
                    _tokens -= tokens;
                    return;
                }
                wait = TimeSpan.FromSeconds((_tokens >= tokens ? 0 : (tokens - _tokens) / _refillPerSecond));
            }
            // Cap single sleeps so cancellation stays responsive.
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(wait.TotalMilliseconds, 1000)), ct);
        }
    }

    // Rough token estimate for pacing only (never billed against): ~4 chars/token.
    public static double EstimateTokens(string text) => Math.Max(1, (text?.Length ?? 0) / 4.0);
}
