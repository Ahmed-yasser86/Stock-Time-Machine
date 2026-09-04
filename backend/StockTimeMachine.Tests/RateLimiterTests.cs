using System.Diagnostics;
using Xunit;
using StockTimeMachine;

namespace StockTimeMachine.Tests;

public class RateLimiterTests
{
    [Fact]
    public void EstimateTokens_ScalesWithLength()
    {
        Assert.Equal(1, TokenBucketRateLimiter.EstimateTokens(""));
        Assert.Equal(25, TokenBucketRateLimiter.EstimateTokens(new string('x', 100)));
    }

    [Fact]
    public async Task WaitAsync_BudgetAvailable_ReturnsImmediately()
    {
        var limiter = new TokenBucketRateLimiter(60000);
        var sw = Stopwatch.StartNew();
        await limiter.WaitAsync(100);
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"took {sw.Elapsed}");
    }

    [Fact]
    public async Task WaitAsync_ExhaustedBudget_WaitsForRefill()
    {
        // 3600/min = 60 tokens/sec: drain the bucket, then one more token
        // must wait roughly a second instead of failing.
        var limiter = new TokenBucketRateLimiter(3600);
        await limiter.WaitAsync(3600);
        var sw = Stopwatch.StartNew();
        await limiter.WaitAsync(60);
        sw.Stop();
        Assert.True(sw.Elapsed >= TimeSpan.FromMilliseconds(700), $"only waited {sw.Elapsed}");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15), $"waited {sw.Elapsed}");
    }

    [Fact]
    public async Task WaitAsync_Cancelled_ThrowsPromptly()
    {
        var limiter = new TokenBucketRateLimiter(60);
        await limiter.WaitAsync(60);
        using var cts = new CancellationTokenSource(500);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => limiter.WaitAsync(6000, cts.Token));
    }
}
