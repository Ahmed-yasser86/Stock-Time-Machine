using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using StockTimeMachine;

namespace StockTimeMachine.Tests;

// Recorded-payload tests: realistic provider JSON is asserted against parsing,
// cutoff, and error-mapping rules without ever touching live quota.
// (CI must never spend the 25/day Alpha Vantage budget.)
public class ProviderFixtureTests
{
    private static IConfiguration Config(params (string Key, string Value)[] pairs)
    {
        var dict = pairs.ToDictionary(p => p.Key, p => (string?)p.Value);
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static IConfiguration AlphaConfig() =>
        Config(("AlphaVantage:ApiKey", "test"), ("AlphaVantage:BaseUrl", "https://www.alphavantage.co/query"),
            ("AlphaVantage:PaceSeconds", "0"));

    private static IConfiguration SecConfig() =>
        Config(("SecEdgar:BaseUrl", "https://data.sec.gov"));

    private const string DailySeries = """
        {
          "Meta Data": {"2. Symbol": "TSLA"},
          "Time Series (Daily)": {
            "2020-01-16": {"1. open": "101.00", "2. high": "102.00", "3. low": "100.00", "4. close": "101.50", "5. volume": "1100"},
            "2020-01-15": {"1. open": "99.00", "2. high": "101.00", "3. low": "98.00", "4. close": "100.00", "5. volume": "1000"},
            "2020-01-14": {"1. open": "98.00", "2. high": "99.50", "3. low": "97.00", "4. close": "98.75", "5. volume": "900"},
            "2020-01-13": {"1. open": "bad", "2. high": "99.50", "3. low": "97.00", "4. close": "98.75", "5. volume": "900"}
          }
        }
        """;

    [Fact]
    public async Task AlphaVantage_ParsesFiltersAndSorts()
    {
        var provider = new AlphaVantageProvider(
            new HttpClient(new StubHttpMessageHandler(DailySeries)),
            NullLogger<AlphaVantageProvider>.Instance, AlphaConfig());

        var result = await provider.GetDailyPrices("tsla", new DateOnly(2020, 1, 15), 365);

        Assert.Equal(2, result.Count); // future row excluded, malformed row skipped
        Assert.Equal(new DateOnly(2020, 1, 15), result[0].Date);
        Assert.Equal(100.00m, result[0].Close);
        Assert.Equal("TSLA", result[0].CompanySymbol);
        Assert.Equal(new DateOnly(2020, 1, 14), result[1].Date);
    }

    [Fact]
    public async Task AlphaVantage_RateLimitInformation_Throws()
    {
        var provider = new AlphaVantageProvider(
            new HttpClient(new StubHttpMessageHandler(
                """{"Information": "Thank you for using Alpha Vantage! Our standard API rate limit is 25 requests per day."}""")),
            NullLogger<AlphaVantageProvider>.Instance, AlphaConfig());

        await Assert.ThrowsAsync<RateLimitExceededException>(
            () => provider.GetDailyPrices("TSLA", new DateOnly(2020, 1, 15), 30));
    }

    [Fact]
    public async Task AlphaVantage_PremiumDenialOnFull_RetriesCompactOnce()
    {
        // Free keys are denied outputsize=full ("premium feature"): the provider
        // retries once with compact, then serves those rows.
        var handler = new RoutedHttpMessageHandler()
            .When(r => r.RequestUri!.Query.Contains("outputsize=full"),
                """{"Information": "The outputsize=full parameter value is a premium feature."}""")
            .When(r => r.RequestUri!.Query.Contains("outputsize=compact"), DailySeries);
        var provider = new AlphaVantageProvider(
            new HttpClient(handler),
            NullLogger<AlphaVantageProvider>.Instance, AlphaConfig());

        var result = await provider.GetDailyPrices("TSLA", new DateOnly(2020, 1, 15), 365);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task AlphaVantage_PremiumDenialOnCompact_ReturnsEmpty()
    {
        var provider = new AlphaVantageProvider(
            new HttpClient(new StubHttpMessageHandler(
                """{"Information": "This is a premium endpoint."}""")),
            NullLogger<AlphaVantageProvider>.Instance, AlphaConfig());

        // Compact is free-tier: a premium notice here means something else —
        // honest empty, not a fabricated rate-limit error.
        Assert.Empty(await provider.GetDailyPrices("TSLA", new DateOnly(2020, 1, 15), 30));
    }

    [Fact]
    public async Task AlphaVantage_ErrorMessage_ThrowsExternal()
    {
        var provider = new AlphaVantageProvider(
            new HttpClient(new StubHttpMessageHandler(
                """{"Error Message": "Invalid API call."}""")),
            NullLogger<AlphaVantageProvider>.Instance, AlphaConfig());

        await Assert.ThrowsAsync<ExternalProviderException>(
            () => provider.GetDailyPrices("TSLA", new DateOnly(2020, 1, 15), 30));
    }

    [Fact]
    public async Task AlphaVantage_Http429_ThrowsRateLimit()
    {
        var provider = new AlphaVantageProvider(
            new HttpClient(new StubHttpMessageHandler("{}", HttpStatusCode.TooManyRequests)),
            NullLogger<AlphaVantageProvider>.Instance, AlphaConfig());

        await Assert.ThrowsAsync<RateLimitExceededException>(
            () => provider.GetDailyPrices("TSLA", new DateOnly(2020, 1, 15), 30));
    }

    [Fact]
    public async Task AlphaVantage_MissingKey_ReturnsEmptyWithoutHttp()
    {
        var provider = new AlphaVantageProvider(
            new HttpClient(new StubHttpMessageHandler("SHOULD-NOT-BE-CALLED", HttpStatusCode.InternalServerError)),
            NullLogger<AlphaVantageProvider>.Instance, Config());

        var result = await provider.GetDailyPrices("TSLA", new DateOnly(2020, 1, 15), 30);

        Assert.Empty(result);
    }

    private const string Submissions = """
        {
          "name": "Tesla, Inc.",
          "tickers": ["TSLA", "TSLAQ"],
          "exchanges": ["Nasdaq"],
          "sicDescription": "Motor Vehicles",
          "filings": {"recent": {
            "form": ["10-K", "8-K", "4", "10-Q/A"],
            "filingDate": ["2020-01-10", "2020-01-20", "2020-01-10", "2019-11-01"],
            "accessionNumber": ["0000000001", "0000000002", "0000000003", "0000000004"],
            "periodOfReport": ["2019-12-31", "", "", "2019-09-30"]
          }}
        }
        """;

    [Fact]
    public async Task SecEdgar_FiltersFormsAndCutoff()
    {
        var provider = new SecEdgarProvider(
            new HttpClient(new StubHttpMessageHandler(Submissions)),
            NullLogger<SecEdgarProvider>.Instance, SecConfig());

        var result = await provider.GetCompanyFilings("1318605", new DateOnly(2020, 1, 15));

        // 8-K of Jan-20 excluded (after asOf), Form 4 excluded (not allowlisted).
        Assert.Equal(2, result.Count);
        Assert.Contains(result, f => f.FormType == "10-K" && f.AccessionNumber == "0000000001");
        Assert.Contains(result, f => f.FormType == "10-Q/A");
        Assert.All(result, f => Assert.StartsWith("https://www.sec.gov/Archives/edgar/data/1318605/", f.Url));
    }

    [Fact]
    public async Task SecEdgar_Profile_PicksFirstTickerUppercase()
    {
        var provider = new SecEdgarProvider(
            new HttpClient(new StubHttpMessageHandler(Submissions)),
            NullLogger<SecEdgarProvider>.Instance, SecConfig());

        var profile = await provider.GetCompanyProfile("1318605");

        Assert.NotNull(profile);
        Assert.Equal("TSLA", profile!.Symbol);
        Assert.Equal("Tesla, Inc.", profile.Name);
        Assert.Equal("0001318605", profile.Cik);
    }

    [Fact]
    public async Task SecEdgar_Http429_ThrowsRateLimit()
    {
        var provider = new SecEdgarProvider(
            new HttpClient(new StubHttpMessageHandler("{}", HttpStatusCode.TooManyRequests)),
            NullLogger<SecEdgarProvider>.Instance, SecConfig());

        await Assert.ThrowsAsync<RateLimitExceededException>(
            () => provider.GetCompanyFilings("1318605", new DateOnly(2020, 1, 15)));
    }

    private static IConfiguration CloudConfig() =>
        Config(("Gdelt:ApiKey", "test-key"), ("Gdelt:CloudBaseUrl", "https://gdeltcloud.com"));

    private const string CloudSearch = """
        {"success": true, "query": "MSFT", "count": 2, "data": [
          {"entity_id": "e_wrong", "name": "Microsoft", "identifiers": {}},
          {"entity_id": "e_msft", "name": "Microsoft Corporation",
           "identifiers": {"ticker": ["MSFT"], "us_sec_cik": ["789019"]}}
        ]}
        """;

    private const string CloudStories = """
        {"success": true, "data": [
          {"id": "s1", "title": "Story past", "story_date": "2020-01-10",
           "url": "https://gdeltcloud.com/stories/s1",
           "top_articles": [
             {"title": "Past article", "url": "https://example.com/a1", "domain": "example.com"},
             {"title": "", "url": "https://example.com/bad", "domain": "example.com"}]},
          {"id": "s2", "title": "Story future", "story_date": "2020-02-01",
           "url": "https://gdeltcloud.com/stories/s2",
           "top_articles": [
             {"title": "Future article", "url": "https://example.com/a2", "domain": "example.com"}]}
        ]}
        """;

    private static GdeltCloudNewsProvider CloudProvider(RoutedHttpMessageHandler handler) =>
        new(new HttpClient(handler), NullLogger<GdeltCloudNewsProvider>.Instance, CloudConfig());

    [Fact]
    public async Task GdeltCloud_NoKey_ReturnsEmpty()
    {
        var provider = new GdeltCloudNewsProvider(
            new HttpClient(new StubHttpMessageHandler("SHOULD-NOT-BE-CALLED", System.Net.HttpStatusCode.InternalServerError)),
            NullLogger<GdeltCloudNewsProvider>.Instance, Config());

        Assert.False(provider.IsConfigured);
        Assert.Empty(await provider.SearchAsync("MSFT", new DateOnly(2020, 1, 15)));
    }

    private const string CloudSearchMismatch = """
        {"success": true, "query": "ZZZZ", "count": 1, "data": [
          {"entity_id": "e_other", "name": "Something Else", "identifiers": {"ticker": ["OTHER"]}}]}
        """;

    [Fact]
    public async Task GdeltCloud_TickerMismatch_ReturnsEmpty()
    {
        var handler = new RoutedHttpMessageHandler()
            .When(r => r.RequestUri!.AbsolutePath.EndsWith("/search"), CloudSearchMismatch);
        var provider = CloudProvider(handler);

        var result = await provider.SearchAsync("ZZZZ", new DateOnly(2020, 1, 15));

        Assert.Empty(result);
        Assert.Equal(1, handler.Calls); // stories never fetched without a ticker match
    }

    [Fact]
    public async Task GdeltCloud_MapsStories_FiltersFuture_SetsIdentity()
    {
        // Per-day traversal: the fixture answers only for the story's own day.
        var handler = new RoutedHttpMessageHandler()
            .When(r => r.RequestUri!.AbsolutePath.EndsWith("/search"), CloudSearch)
            .When(r => r.RequestUri!.AbsolutePath.EndsWith("/stories") &&
                r.RequestUri.Query.Contains("date_start=2020-01-10"), CloudStories)
            .When(r => r.RequestUri!.AbsolutePath.EndsWith("/stories"),
                """{"success": true, "data": []}""");
        var provider = CloudProvider(handler);

        var result = await provider.SearchAsync("msft", new DateOnly(2020, 1, 15));

        var single = Assert.Single(result);
        Assert.Equal("Past article", single.Title);
        Assert.Equal("https://example.com/a1", single.Url);
        Assert.Equal("GDELT Cloud via example.com", single.Source);
        Assert.Equal("MSFT", single.CompanySymbol);
        Assert.Equal(new DateTime(2020, 1, 10, 0, 0, 0, DateTimeKind.Utc), single.PublishedAt);
        Assert.False(string.IsNullOrEmpty(single.Id));
        Assert.Equal(9, handler.Calls); // 1 resolve + 8 traversed days
    }

    private const string CloudSearchNoTicker = """
        {"success": true, "query": "MSFT", "count": 1, "data": [
          {"entity_id": "e_other", "name": "MSFT Something", "identifiers": {}}]}
        """;

    [Fact]
    public async Task GdeltCloud_ResolvesByCompanyNameWhenSymbolMisses()
    {
        // Live behavior: q=MSFT alone does not resolve, q=Microsoft does.
        var handler = new RoutedHttpMessageHandler()
            .When(r => r.RequestUri!.AbsolutePath.EndsWith("/search") && r.RequestUri.Query.Contains("q=Microsoft"),
                CloudSearch)
            .When(r => r.RequestUri!.AbsolutePath.EndsWith("/search"), CloudSearchNoTicker)
            .When(r => r.RequestUri!.AbsolutePath.EndsWith("/stories") &&
                r.RequestUri.Query.Contains("date_start=2020-01-10"), CloudStories)
            .When(r => r.RequestUri!.AbsolutePath.EndsWith("/stories"),
                """{"success": true, "data": []}""");
        var provider = CloudProvider(handler);

        var result = await provider.SearchAsync("MSFT", "Microsoft Corporation", new DateOnly(2020, 1, 15));

        var single = Assert.Single(result);
        Assert.Equal("Past article", single.Title);
        Assert.Equal(9, handler.Calls); // name search resolves; symbol fallback skipped; 8 traversed days
    }

    [Fact]
    public async Task GdeltCloud_ServerError_ReturnsEmpty()
    {
        var handler = new RoutedHttpMessageHandler()
            .When(_ => true, "boom", System.Net.HttpStatusCode.InternalServerError);
        var provider = CloudProvider(handler);

        Assert.Empty(await provider.SearchAsync("MSFT", new DateOnly(2020, 1, 15)));
    }

    private static IConfiguration ArcticConfig(bool enabled = true) =>
        Config(("Social:ArcticShift:BaseUrl", "https://arctic-shift.photon-reddit.com"),
            ("Social:ArcticShift:Enabled", enabled ? "true" : "false"),
            ("Social:ArcticShift:Subreddits", "wallstreetbets,stocks"));

    private const string ArcticPosts = """
        {"data": [
          {"id": "p1", "title": "TSLA moon", "selftext": "To the moon!",
           "permalink": "/r/wallstreetbets/comments/p1/x/", "created_utc": 1579132800,
           "score": 500, "num_comments": 120, "link_flair_text": "Gain"},
          {"id": "p2", "title": "Old post", "selftext": "",
           "permalink": "/r/wallstreetbets/comments/p2/x/", "created_utc": 1577836800,
           "score": 10, "num_comments": 2, "link_flair_text": null},
          {"id": "", "title": "Undated", "selftext": "",
           "permalink": "/r/wallstreetbets/comments/p3/x/",
           "score": 5, "num_comments": 0, "link_flair_text": null}
        ]}
        """;

    [Fact]
    public async Task ArcticShift_FiltersWindow_DropsUndated()
    {
        // 1579132800 = 2020-01-16 UTC; 1577836800 = 2020-01-01 UTC.
        var provider = new ArcticShiftProvider(
            new HttpClient(new StubHttpMessageHandler(ArcticPosts)),
            NullLogger<ArcticShiftProvider>.Instance, ArcticConfig());

        var result = await provider.GetSignals("TSLA", "Tesla, Inc.",
            new DateOnly(2020, 1, 10), new DateOnly(2020, 1, 20));

        var single = Assert.Single(result);
        Assert.Equal("TSLA moon", single.Title);
        Assert.Equal("r/wallstreetbets", single.Community);
        Assert.Equal("Gain", single.Flair);
        Assert.Equal("https://www.reddit.com/r/wallstreetbets/comments/p1/x/", single.Url);
        Assert.Equal("TSLA", single.CompanySymbol);
    }

    [Fact]
    public async Task ArcticShift_Disabled_ReturnsEmpty()
    {
        var provider = new ArcticShiftProvider(
            new HttpClient(new StubHttpMessageHandler("SHOULD-NOT-BE-CALLED", System.Net.HttpStatusCode.InternalServerError)),
            NullLogger<ArcticShiftProvider>.Instance, ArcticConfig(enabled: false));

        Assert.Empty(await provider.GetSignals("TSLA", null,
            new DateOnly(2020, 1, 10), new DateOnly(2020, 1, 20)));
    }

    private static IConfiguration MarketAuxConfig() =>
        Config(("MarketAux:ApiKey", "test-key"), ("MarketAux:BaseUrl", "https://api.marketaux.com"));

    private const string MarketAuxFeed = """
        {"meta": {"found": 3, "returned": 3, "limit": 3, "page": 1}, "data": [
          {"uuid": "aaa-1", "title": "Past story", "description": "d", "snippet": "s",
           "url": "https://example.com/past", "language": "en",
           "published_at": "2020-01-10T12:00:00.000000Z", "source": "example.com",
           "entities": [{"symbol": "TSLA", "sentiment_score": 0.5}]},
          {"uuid": "bbb-2", "title": "Future story", "description": "d", "snippet": "s",
           "url": "https://example.com/future", "language": "en",
           "published_at": "2020-02-01T12:00:00.000000Z", "source": "example.com",
           "entities": []},
          {"uuid": "ccc-3", "title": "", "description": "d", "snippet": "s",
           "url": "https://example.com/empty", "language": "en",
           "published_at": "2020-01-10T12:00:00.000000Z", "source": "example.com",
           "entities": []}
        ]}
        """;

    [Fact]
    public async Task MarketAux_MapsFeed_FiltersFuture_SetsIdentity()
    {
        var provider = new MarketAuxNewsProvider(
            new HttpClient(new StubHttpMessageHandler(MarketAuxFeed)),
            NullLogger<MarketAuxNewsProvider>.Instance, MarketAuxConfig());

        var result = await provider.SearchAsync("tsla", new DateOnly(2020, 1, 15));

        var single = Assert.Single(result);
        Assert.Equal("Past story", single.Title);
        Assert.Equal("mux-aaa-1", single.Id);
        Assert.Equal(0.5m, single.SentimentScore);
        Assert.Equal("MarketAux via example.com", single.Source);
        Assert.Equal("TSLA", single.CompanySymbol);
        Assert.Equal(new DateTime(2020, 1, 10, 12, 0, 0, DateTimeKind.Utc), single.PublishedAt);
    }

    [Fact]
    public async Task MarketAux_NoKey_ReturnsEmpty()
    {
        var provider = new MarketAuxNewsProvider(
            new HttpClient(new StubHttpMessageHandler("SHOULD-NOT-BE-CALLED", System.Net.HttpStatusCode.InternalServerError)),
            NullLogger<MarketAuxNewsProvider>.Instance, Config());

        Assert.False(provider.IsConfigured);
        Assert.Empty(await provider.SearchAsync("TSLA", new DateOnly(2020, 1, 15)));
    }

    [Fact]
    public async Task MarketAux_Http429_ThrowsRateLimit()
    {
        var provider = new MarketAuxNewsProvider(
            new HttpClient(new StubHttpMessageHandler("{}", System.Net.HttpStatusCode.TooManyRequests)),
            NullLogger<MarketAuxNewsProvider>.Instance, MarketAuxConfig());

        await Assert.ThrowsAsync<RateLimitExceededException>(
            () => provider.SearchAsync("TSLA", new DateOnly(2020, 1, 15)));
    }

    [Fact]
    public async Task MarketAux_EmptyData_ReturnsEmpty()
    {
        var provider = new MarketAuxNewsProvider(
            new HttpClient(new StubHttpMessageHandler("""{"meta": {"found": 0}, "data": []}""")),
            NullLogger<MarketAuxNewsProvider>.Instance, MarketAuxConfig());

        Assert.Empty(await provider.SearchAsync("TSLA", new DateOnly(2020, 1, 15)));
    }

    [Fact]
    public async Task SecEdgar_MissingFilingsNode_ReturnsEmpty()
    {
        var provider = new SecEdgarProvider(
            new HttpClient(new StubHttpMessageHandler("""{"name": "X"}""")),
            NullLogger<SecEdgarProvider>.Instance, SecConfig());

        var result = await provider.GetCompanyFilings("1318605", new DateOnly(2020, 1, 15));

        Assert.Empty(result);
    }
}
