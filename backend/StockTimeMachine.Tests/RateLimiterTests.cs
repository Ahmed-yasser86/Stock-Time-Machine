using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using StockTimeMachine;

namespace StockTimeMachine.Tests;

// Global adaptive limiter: AIMD rhythm (batch halved, spacing doubled per
// 429; gradual recovery on streaks), token budgets, and exactly-once batches.
public class RateLimiterTests
{
    private static AdaptiveRateLimiter Limiter(
        int batch = 4, int maxBatch = 4, int delayMs = 0, int minDelayMs = 0,
        double tpm = 0, int maxAttempts = 5, int streak = 20) =>
        new(new RatePolicy
        {
            InitialBatchSize = batch,
            MaxBatchSize = maxBatch,
            InitialDelayMs = delayMs,
            MinDelayMs = minDelayMs,
            TokensPerMinute = tpm,
            MaxAttempts = maxAttempts,
            RecoveryStreak = streak,
        });

    private static TimeSpan? NeverThrottle(Exception ex) => null;

    [Fact]
    public void Throttle_ShrinksBatchAndStretchesDelay()
    {
        var limiter = Limiter(batch: 4, maxBatch: 8, delayMs: 1000);

        var pause = limiter.ReportThrottled(TimeSpan.FromSeconds(5));

        Assert.Equal(TimeSpan.FromSeconds(5), pause);
        Assert.Equal(2, limiter.BatchSize);
        Assert.Equal(2000, limiter.DelayMs);
        Assert.Equal(1, limiter.Recent429s);
        Assert.Equal(0, limiter.SuccessStreak);
    }

    [Fact]
    public void Throttle_WithoutHeader_UsesBackoffFloor()
    {
        var limiter = Limiter(batch: 1, maxBatch: 4, delayMs: 500);

        var pause = limiter.ReportThrottled(null);

        Assert.True(pause >= TimeSpan.FromMilliseconds(1000), $"pause {pause}");
        Assert.Equal(1, limiter.BatchSize); // already at floor
        Assert.Equal(1000, limiter.DelayMs);
    }

    [Fact]
    public void SuccessStreak_RecoversGradually()
    {
        var limiter = Limiter(batch: 1, maxBatch: 4, delayMs: 1000, streak: 4);
        limiter.ReportThrottled(null); // delay 2000
        Assert.Equal(2000, limiter.DelayMs);

        for (int i = 0; i < 4; i++)
            limiter.ReportSuccess();

        Assert.Equal(1800, limiter.DelayMs); // 10% ease, never a jump
        Assert.Equal(2, limiter.BatchSize);
    }

    [Fact]
    public async Task Acquire_SpacesCalls()
    {
        var limiter = Limiter(delayMs: 300);

        var sw = Stopwatch.StartNew();
        await limiter.AcquireAsync();
        await limiter.AcquireAsync();
        sw.Stop();

        Assert.True(sw.Elapsed >= TimeSpan.FromMilliseconds(250), $"only spaced {sw.Elapsed}");
    }

    [Fact]
    public async Task Acquire_TokenBudget_WaitsInsteadOfFailing()
    {
        var limiter = Limiter(tpm: 3600); // 60 tokens/sec
        await limiter.AcquireAsync(3600);

        var sw = Stopwatch.StartNew();
        await limiter.AcquireAsync(60);
        sw.Stop();

        Assert.True(sw.Elapsed >= TimeSpan.FromMilliseconds(700), $"only waited {sw.Elapsed}");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15), $"waited {sw.Elapsed}");
    }

    [Fact]
    public async Task ExecuteBatch_AllItemsExactlyOnce()
    {
        var limiter = Limiter(batch: 4, maxBatch: 4);
        var seen = new System.Collections.Concurrent.ConcurrentBag<int>();

        var results = await limiter.ExecuteBatchAsync(
            new[] { 1, 2, 3, 4, 5 },
            (item, ct) => { seen.Add(item); return Task.FromResult(item * 10); },
            NeverThrottle,
            NullLogger.Instance);

        Assert.Equal(new[] { 10, 20, 30, 40, 50 }, results.OrderBy(x => x));
        Assert.Equal(5, seen.Count);
    }

    [Fact]
    public async Task ExecuteBatch_ThrottledItem_RequeuesUnderSlowerRhythm()
    {
        var limiter = Limiter(batch: 4, maxBatch: 4, delayMs: 0, maxAttempts: 5);
        var attempts = 0;

        var results = await limiter.ExecuteBatchAsync(
            new[] { 1 },
            (int item, CancellationToken ct) =>
            {
                attempts++;
                if (attempts < 3)
                    throw new RateLimitExceededException("throttled", TimeSpan.Zero);
                return Task.FromResult(item);
            },
            ex => ex is RateLimitExceededException r ? r.RetryAfter ?? TimeSpan.Zero : null,
            NullLogger.Instance);

        Assert.Equal(new[] { 1 }, results);
        Assert.Equal(3, attempts);
        Assert.True(limiter.DelayMs > 0);
        Assert.True(limiter.BatchSize < 4);
    }

    [Fact]
    public async Task ExecuteBatch_NonThrottleError_FailsFast()
    {
        var limiter = Limiter();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            limiter.ExecuteBatchAsync<int, int>(
                new[] { 1, 2 },
                (item, ct) => throw new InvalidOperationException("real failure"),
                NeverThrottle,
                NullLogger.Instance));
    }

    [Fact]
    public async Task ExecuteBatch_ExhaustedAttempts_Propagates()
    {
        var limiter = Limiter(maxAttempts: 2);

        await Assert.ThrowsAsync<RateLimitExceededException>(() =>
            limiter.ExecuteBatchAsync<int, int>(
                new[] { 1 },
                (item, ct) => throw new RateLimitExceededException("down", TimeSpan.Zero),
                ex => ex is RateLimitExceededException r ? r.RetryAfter ?? TimeSpan.Zero : null,
                NullLogger.Instance));
    }

    [Fact]
    public void Registry_CachesPerName()
    {
        RateLimiterRegistry.Reset();
        var config = new ConfigurationBuilder().Build();
        try
        {
            var a = RateLimiterRegistry.Get("gdelt", config);
            var b = RateLimiterRegistry.Get("gdelt", config);
            Assert.Same(a, b);
            Assert.NotSame(a, RateLimiterRegistry.Get("jina", config));
        }
        finally
        {
            RateLimiterRegistry.Reset();
        }
    }

    [Fact]
    public void EstimateTokens_ScalesWithLength()
    {
        Assert.Equal(1, AdaptiveRateLimiter.EstimateTokens(""));
        Assert.Equal(25, AdaptiveRateLimiter.EstimateTokens(new string('x', 100)));
    }
}
