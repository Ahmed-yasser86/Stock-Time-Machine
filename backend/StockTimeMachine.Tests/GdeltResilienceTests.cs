using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using StockTimeMachine;

namespace StockTimeMachine.Tests;

public class GdeltResilienceTests
{
    private static IConfiguration CloudConfig(int paceMs = 0) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gdelt:ApiKey"] = "test-key",
            ["Gdelt:CloudBaseUrl"] = "https://gdeltcloud.com",
            ["Gdelt:MinRequestIntervalMs"] = paceMs.ToString(),
        }).Build();

    private const string CloudSearch = """
        {"success": true, "query": "MSFT", "count": 1, "data": [
          {"entity_id": "e_msft", "name": "Microsoft Corporation",
           "identifiers": {"ticker": ["MSFT"]}}
        ]}
        """;

    private const string CloudStories = """
        {"success": true, "data": [
          {"id": "s1", "title": "Story", "story_date": "2020-01-10",
           "top_articles": [
             {"title": "Past article", "url": "https://example.com/a1", "domain": "example.com"}]}
        ]}
        """;

    [Fact]
    public async Task Cloud_RecoversAfterTransient429s()
    {
        int searches = 0;
        var handler = new RoutedHttpMessageHandler()
            .When(r => r.RequestUri!.AbsolutePath.EndsWith("/search") && ++searches <= 2,
                "", HttpStatusCode.TooManyRequests,
                new Dictionary<string, string> { ["Retry-After"] = "0" })
            .When(r => r.RequestUri!.AbsolutePath.EndsWith("/search"), CloudSearch)
            .When(r => r.RequestUri!.AbsolutePath.EndsWith("/stories"), CloudStories);
        var provider = new GdeltCloudNewsProvider(
            new HttpClient(handler), NullLogger<GdeltCloudNewsProvider>.Instance, CloudConfig());

        var result = await provider.SearchAsync("MSFT", new DateOnly(2020, 1, 15));

        var single = Assert.Single(result);
        Assert.Equal("Past article", single.Title);
        Assert.Equal(4, handler.Calls); // 2 throttled + resolve + stories
    }

    [Fact]
    public async Task Cloud_Persistent429_PropagatesAfterBoundedRetries()
    {
        var handler = new RoutedHttpMessageHandler()
            .When(_ => true, "", HttpStatusCode.TooManyRequests,
                new Dictionary<string, string> { ["Retry-After"] = "0" });
        var provider = new GdeltCloudNewsProvider(
            new HttpClient(handler), NullLogger<GdeltCloudNewsProvider>.Instance, CloudConfig());

        await Assert.ThrowsAsync<RateLimitExceededException>(
            () => provider.SearchAsync("MSFT", new DateOnly(2020, 1, 15)));
        Assert.Equal(4, handler.Calls); // 1 initial + 3 retries, then give up
    }

    [Fact]
    public async Task Cloud_RetryAfterHeader_IsHonored()
    {
        var handler = new RoutedHttpMessageHandler()
            .When(r => r.RequestUri!.AbsolutePath.EndsWith("/search"),
                "", HttpStatusCode.TooManyRequests,
                new Dictionary<string, string> { ["Retry-After"] = "2" })
            .When(r => r.RequestUri!.AbsolutePath.EndsWith("/stories"), CloudStories);
        var provider = new GdeltCloudNewsProvider(
            new HttpClient(handler), NullLogger<GdeltCloudNewsProvider>.Instance, CloudConfig());
        // Entity resolve fails over to symbol query; both 429 once each side.
        // Simplify: search succeeds on retry — covered above; here assert the wait.
        var sw = Stopwatch.StartNew();
        await Assert.ThrowsAsync<RateLimitExceededException>(
            () => provider.SearchAsync("MSFT", new DateOnly(2020, 1, 15)));
        sw.Stop();
        // 4 attempts × ~2s server-asked waits (resolve retried: company+symbol queries).
        Assert.True(sw.Elapsed >= TimeSpan.FromSeconds(3), $"only waited {sw.Elapsed}");
    }

    [Fact]
    public async Task Cloud_Pacing_SpacesRapidSearches()
    {
        var handler = new RoutedHttpMessageHandler()
            .When(r => r.RequestUri!.AbsolutePath.EndsWith("/search"), CloudSearch)
            .When(r => r.RequestUri!.AbsolutePath.EndsWith("/stories"), CloudStories);
        var provider = new GdeltCloudNewsProvider(
            new HttpClient(handler), NullLogger<GdeltCloudNewsProvider>.Instance, CloudConfig(paceMs: 400));

        var sw = Stopwatch.StartNew();
        await provider.SearchAsync("MSFT", new DateOnly(2020, 1, 15));
        await provider.SearchAsync("MSFT", new DateOnly(2020, 1, 15));
        sw.Stop();

        Assert.True(sw.Elapsed >= TimeSpan.FromMilliseconds(300), $"only spaced {sw.Elapsed}");
    }

    [Fact]
    public void ParseRetryAfter_HandlesForms()
    {
        using var seconds = new HttpResponseMessage();
        seconds.Headers.TryAddWithoutValidation("Retry-After", "120");
        Assert.Equal(TimeSpan.FromSeconds(120), GdeltResilience.ParseRetryAfter(seconds.Headers));

        using var missing = new HttpResponseMessage();
        Assert.Null(GdeltResilience.ParseRetryAfter(missing.Headers));

        using var garbage = new HttpResponseMessage();
        garbage.Headers.TryAddWithoutValidation("Retry-After", "soon");
        Assert.Null(GdeltResilience.ParseRetryAfter(garbage.Headers));
    }
}
