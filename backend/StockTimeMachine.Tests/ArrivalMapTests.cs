using Xunit;
using StockTimeMachine;

namespace StockTimeMachine.Tests;

public class ArrivalMapTests
{
    private static DateTime Utc(int y, int m, int d, int h = 0) =>
        new(y, m, d, h, 0, 0, DateTimeKind.Utc);

    private static MoveEvidence Sample() => new()
    {
        Filings = new List<SecFiling>
        {
            new() { AccessionNumber = "f1", FormType = "10-K", FiledAt = Utc(2020, 1, 28), Url = "https://example.com/f1", CompanySymbol = "TSLA" },
        },
        News = new List<NewsArticle>
        {
            new() { Id = "n1", Title = "t", Source = "GDELT", PublishedAt = Utc(2020, 1, 25, 12), Url = "https://example.com/n1", CompanySymbol = "TSLA" },
        },
        Social = new List<SocialSignal>
        {
            new() { Id = "s1", Provider = "Arctic Shift", Community = "r/stocks", Title = "t", Url = "", CreatedAt = Utc(2020, 1, 29, 18), CompanySymbol = "TSLA" },
        },
    };

    [Fact]
    public void Build_OrdersLayersByFirstAppearance_WithLags()
    {
        var entries = ArrivalMap.Build(new DateOnly(2020, 2, 1), Sample());

        Assert.Equal(
            new[] { "news", "regulatory", "social", "market" },
            entries.Where(e => e.State == "observed").Select(e => e.Layer));
        Assert.Equal(0, entries[0].LagHours);
        Assert.Equal(60, entries[1].LagHours); // Jan 25 12:00 -> Jan 28 00:00
        Assert.Equal(102, entries[2].LagHours); // Jan 25 12:00 -> Jan 29 18:00
        Assert.Equal(156, entries[3].LagHours); // Jan 25 12:00 -> Feb 1 00:00
    }

    [Fact]
    public void Build_SilentLayer_IsUnknownNotZero()
    {
        var evidence = Sample();
        evidence.Social.Clear();

        var entries = ArrivalMap.Build(new DateOnly(2020, 2, 1), evidence);

        var social = Assert.Single(entries, e => e.Layer == "social");
        Assert.Equal("silent", social.State);
        Assert.Null(social.FirstSeen);
        Assert.Null(social.LagHours);
    }

    [Fact]
    public void Build_EmptyEvidence_OnlyMarketObserved()
    {
        var entries = ArrivalMap.Build(new DateOnly(2020, 2, 1), new MoveEvidence());

        Assert.Single(entries, e => e.State == "observed");
        Assert.Equal("market", entries.Single(e => e.State == "observed").Layer);
        Assert.Equal(3, entries.Count(e => e.State == "silent"));
    }

    // Apple 2026-06-09 shape: a 2015 filing in storage must not become
    // movement-level evidence — first-seen, counts, and tiers stay
    // window-scoped (reg-v1).
    [Fact]
    public void Build_RegulatoryLayer_IsWindowScoped_IgnoresCompanyHistory()
    {
        var evidence = new MoveEvidence
        {
            Filings = new List<SecFiling>
            {
                new() { AccessionNumber = "old", FormType = "10-K", FiledAt = Utc(2015, 7, 20), Url = "https://example.com/old", CompanySymbol = "AAPL" },
                new() { AccessionNumber = "f1", FormType = "8-K", FiledAt = Utc(2026, 5, 20), Url = "https://example.com/f1", CompanySymbol = "AAPL" },
                new() { AccessionNumber = "f2", FormType = "8-K", FiledAt = Utc(2026, 6, 4), Url = "https://example.com/f2", CompanySymbol = "AAPL" },
                new() { AccessionNumber = "f3", FormType = "8-K", FiledAt = Utc(2026, 6, 8), Url = "https://example.com/f3", CompanySymbol = "AAPL" },
            },
        };

        var entries = ArrivalMap.Build(new DateOnly(2026, 6, 9), evidence);

        var regulatory = Assert.Single(entries, e => e.Layer == "regulatory");
        Assert.Equal("observed", regulatory.State);
        Assert.Equal(Utc(2026, 5, 20), regulatory.FirstSeen);
        Assert.Equal(
            "3 filing(s) within the 30-day window (very close: 1, recent: 1, older: 1)",
            regulatory.Detail);
    }

    [Fact]
    public void Build_RegulatoryLayer_EmptyWindow_IsSilentWithWindowDetail()
    {
        var evidence = new MoveEvidence
        {
            Filings = new List<SecFiling>
            {
                new() { AccessionNumber = "old", FormType = "10-K", FiledAt = Utc(2015, 7, 20), Url = "https://example.com/old", CompanySymbol = "AAPL" },
            },
        };

        var entries = ArrivalMap.Build(new DateOnly(2026, 6, 9), evidence);

        var regulatory = Assert.Single(entries, e => e.Layer == "regulatory");
        Assert.Equal("silent", regulatory.State);
        Assert.Null(regulatory.FirstSeen);
        Assert.Equal("no filings within the 30-day window", regulatory.Detail);
    }

    [Fact]
    public void Build_IsDeterministic()
    {
        var first = ArrivalMap.Build(new DateOnly(2020, 2, 1), Sample());
        var second = ArrivalMap.Build(new DateOnly(2020, 2, 1), Sample());

        Assert.Equal(
            first.Select(e => (e.Layer, e.State, e.LagHours)),
            second.Select(e => (e.Layer, e.State, e.LagHours)));
    }
}
