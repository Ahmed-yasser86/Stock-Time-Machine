using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace StockTimeMachine;

// Global adaptive rate limiter: ONE mechanism for every outbound API call.
// Stateful AIMD rhythm control per named scope:
// - each request acquires budget (token bucket when metered) plus spacing;
// - a 429 shrinks the batch, stretches spacing/backoff, and pauses;
// - sustained success recovers gradually, never jumps back.
// Reliability over throughput: pending work waits, never drops (callers bound
// total attempts instead). Provider/model quotas ride on top as RatePolicy.
public class RatePolicy
{
    public int InitialBatchSize { get; set; } = 1;
    public int MaxBatchSize { get; set; } = 4;
    public int InitialDelayMs { get; set; } = 1000;
    public int MinDelayMs { get; set; } = 250;
    public int MaxDelayMs { get; set; } = 30000;
    public double TokensPerMinute { get; set; } = 0;
    public int MaxAttempts { get; set; } = 5;
    public int RecoveryStreak { get; set; } = 20;
    public int MaxBackoffMs { get; set; } = 60000;
}

public class AdaptiveRateLimiter
{
    private readonly RatePolicy _policy;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private int _batchSize;
    private int _delayMs;
    private double _tokens;
    private DateTime _lastTick;
    private DateTime _lastCallUtc = DateTime.MinValue;
    private int _recent429s;
    private int _successStreak;

    public AdaptiveRateLimiter(RatePolicy policy)
    {
        _policy = policy;
        _batchSize = Math.Max(1, Math.Min(policy.InitialBatchSize, policy.MaxBatchSize));
        _delayMs = Math.Max(0, policy.InitialDelayMs);
        _tokens = policy.TokensPerMinute;
        _lastTick = DateTime.UtcNow;
    }

    public int BatchSize { get { lock (_gate) return _batchSize; } }
    public int DelayMs { get { lock (_gate) return _delayMs; } }
    public int MaxAttempts => _policy.MaxAttempts;
    public int Recent429s { get { lock (_gate) return _recent429s; } }
    public int SuccessStreak { get { lock (_gate) return _successStreak; } }

    // Wait for token budget (when metered) and inter-request spacing, then
    // stamp this call so concurrent traffic serializes its rhythm.
    public async Task AcquireAsync(double tokens = 0, CancellationToken ct = default)
    {
        while (true)
        {
            TimeSpan wait;
            lock (_gate)
            {
                var now = DateTime.UtcNow;
                if (_policy.TokensPerMinute > 0)
                {
                    _tokens = Math.Min(_policy.TokensPerMinute,
                        _tokens + (now - _lastTick).TotalSeconds * _policy.TokensPerMinute / 60.0);
                    _lastTick = now;
                }
                var next = _lastCallUtc + TimeSpan.FromMilliseconds(_delayMs);
                var spacing = next > now ? next - now : TimeSpan.Zero;
                double have = _policy.TokensPerMinute > 0 ? _tokens : double.MaxValue;
                if (have >= tokens && spacing == TimeSpan.Zero)
                {
                    if (_policy.TokensPerMinute > 0)
                        _tokens -= tokens;
                    _lastCallUtc = now;
                    return;
                }
                var tokenWait = have >= tokens ? TimeSpan.Zero :
                    TimeSpan.FromSeconds((tokens - have) / (_policy.TokensPerMinute / 60.0));
                wait = spacing > tokenWait ? spacing : tokenWait;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(wait.TotalMilliseconds, 1000)), ct);
        }
    }

    // A 429 means the rhythm is too aggressive: halve the batch, double the
    // spacing, grow the recent-429 count, reset the streak. Returns how long
    // the caller must pause (server-asked Retry-After wins, capped).
    public TimeSpan ReportThrottled(TimeSpan? retryAfter = null)
    {
        lock (_gate)
        {
            _recent429s++;
            _successStreak = 0;
            _batchSize = Math.Max(1, _batchSize / 2);
            _delayMs = Math.Min(_policy.MaxDelayMs, Math.Max(_delayMs, 1) * 2);
            var pause = retryAfter ?? TimeSpan.FromMilliseconds(Math.Min(_delayMs * 2, _policy.MaxBackoffMs));
            if (pause > TimeSpan.FromMilliseconds(_policy.MaxBackoffMs))
                pause = TimeSpan.FromMilliseconds(_policy.MaxBackoffMs);
            return pause;
        }
    }

    // Success feeds gradual recovery: every RecoveryStreak clean calls ease
    // spacing down 10% and reopen one batch slot. Never jumps back.
    public void ReportSuccess()
    {
        lock (_gate)
        {
            _recent429s = 0;
            _successStreak++;
            if (_successStreak >= _policy.RecoveryStreak)
            {
                _successStreak = 0;
                _delayMs = Math.Max(_policy.MinDelayMs, (int)(_delayMs * 0.9));
                _batchSize = Math.Min(_policy.MaxBatchSize, _batchSize + 1);
            }
        }
    }

    // Bounded-parallel batch with adaptation: at most BatchSize in flight,
    // each start spaced by Acquire; a throttled item pauses, shrinks the
    // rhythm, and requeues (items leave the queue only on success, so no
    // request is lost or duplicated). After MaxAttempts per item — or any
    // non-throttle error — siblings cancel and the error propagates for the
    // caller to degrade honestly. Bounded, never infinite.
    public async Task<IReadOnlyList<R>> ExecuteBatchAsync<T, R>(
        IReadOnlyList<T> items,
        Func<T, CancellationToken, Task<R>> operation,
        Func<Exception, TimeSpan?> throttleSignal,
        ILogger? logger,
        CancellationToken ct = default)
    {
        var results = new R[items.Count];
        var attempts = new int[items.Count];
        var queue = new Queue<int>(Enumerable.Range(0, items.Count));
        using var scope = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = scope.Token;

        async Task WorkerAsync()
        {
            while (true)
            {
                int index;
                lock (queue)
                {
                    if (queue.Count == 0 || token.IsCancellationRequested)
                        return;
                    index = queue.Dequeue();
                }
                try
                {
                    await AcquireAsync(0, token);
                    results[index] = await operation(items[index], token);
                    ReportSuccess();
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    lock (queue)
                        queue.Enqueue(index);
                    return;
                }
                catch (Exception ex)
                {
                    var retryAfter = throttleSignal(ex);
                    if (retryAfter is null || ++attempts[index] >= _policy.MaxAttempts)
                    {
                        scope.Cancel();
                        throw;
                    }
                    var pause = ReportThrottled(retryAfter.Value);
                    logger?.LogWarning("Batch throttled; pausing {Pause} (batch {Batch}, delay {Delay}ms)",
                        pause, BatchSize, DelayMs);
                    try
                    {
                        await Task.Delay(pause, token);
                    }
                    catch (OperationCanceledException)
                    {
                        lock (queue)
                            queue.Enqueue(index);
                        return;
                    }
                    lock (queue)
                        queue.Enqueue(index);
                }
            }
        }

        // Workers sized by live batch size; sequential start keeps bursts
        // impossible even as recovery reopens slots.
        var workers = new List<Task>();
        for (int w = 0, slots = Math.Max(1, BatchSize); w < slots; w++)
            workers.Add(Task.Run(WorkerAsync, token));
        await Task.WhenAll(workers);
        return results;
    }

    public static double EstimateTokens(string text) => Math.Max(1, (text?.Length ?? 0) / 4.0);
}

// Shared Retry-After parsing (delta-seconds or HTTP date). Null when absent
// or unparseable — callers fall back to backoff.
public static class RateLimitHeaders
{
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

// One limiter per named scope, process-wide. Policies come from
// `RateLimits:{name}` config with per-provider defaults below; legacy keys
// (AlphaVantage:PaceSeconds, Gdelt:MinRequestIntervalMs,
// Gemini:MinRequestIntervalMs) still win where they exist so current
// deployments and test configs keep working.
public static class RateLimiterRegistry
{
    private static readonly ConcurrentDictionary<string, AdaptiveRateLimiter> Cache = new();

    public static AdaptiveRateLimiter Get(string name, IConfiguration config) =>
        Cache.GetOrAdd(name, _ => new AdaptiveRateLimiter(LoadPolicy(name, config)));

    // Tests and hosts reset between isolated scenarios sharing a process.
    public static void Reset() => Cache.Clear();

    private static int Int(IConfiguration config, string key, int fallback) =>
        int.TryParse(config[key], out var v) ? v : fallback;

    private static RatePolicy LoadPolicy(string name, IConfiguration config)
    {
        // Code defaults are zero-delay (fast, hermetic tests); production
        // rhythm lives in appsettings.json RateLimits so operators tune
        // without rebuilding. Legacy per-provider keys still override.
        var policy = name switch
        {
            "gdelt" => new RatePolicy { InitialBatchSize = 1, MaxBatchSize = 2, InitialDelayMs = 0, MinDelayMs = 0 },
            "alphavantage" => new RatePolicy { InitialBatchSize = 1, MaxBatchSize = 1, InitialDelayMs = 12000, MinDelayMs = 0 },
            "arctic" => new RatePolicy { InitialBatchSize = 1, MaxBatchSize = 1, InitialDelayMs = 0, MinDelayMs = 0 },
            "marketaux" => new RatePolicy { InitialBatchSize = 1, MaxBatchSize = 2, InitialDelayMs = 0, MinDelayMs = 0 },
            "jina" => new RatePolicy { InitialBatchSize = 2, MaxBatchSize = 4, InitialDelayMs = 0, MinDelayMs = 0 },
            "gemini-embed" => new RatePolicy { InitialBatchSize = 2, MaxBatchSize = 4, InitialDelayMs = 0, MinDelayMs = 0, TokensPerMinute = 30000 },
            "gemini-generate" => new RatePolicy { InitialBatchSize = 1, MaxBatchSize = 2, InitialDelayMs = 0, MinDelayMs = 0, TokensPerMinute = 30000 },
            _ => new RatePolicy { InitialDelayMs = 0, MinDelayMs = 0 },
        };
        policy.InitialDelayMs = Int(config, $"RateLimits:{name}:InitialDelayMs", policy.InitialDelayMs);
        policy.MaxBatchSize = Int(config, $"RateLimits:{name}:MaxBatchSize", policy.MaxBatchSize);
        if (Int(config, $"RateLimits:{name}:TokensPerMinute", -1) is int tpm && tpm >= 0)
            policy.TokensPerMinute = tpm;
        // Legacy keys keep existing deployments and test configs working.
        if (name == "gdelt")
            policy.InitialDelayMs = Int(config, "Gdelt:MinRequestIntervalMs", policy.InitialDelayMs);
        if (name == "alphavantage" && double.TryParse(config["AlphaVantage:PaceSeconds"],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pace))
            policy.InitialDelayMs = (int)(pace * 1000);
        if (name.StartsWith("gemini", StringComparison.Ordinal))
            policy.InitialDelayMs = Int(config, "Gemini:MinRequestIntervalMs", policy.InitialDelayMs);
        policy.InitialBatchSize = Math.Max(1, Math.Min(policy.InitialBatchSize, policy.MaxBatchSize));
        return policy;
    }
}
