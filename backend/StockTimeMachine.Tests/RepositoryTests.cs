using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using StockTimeMachine;

namespace StockTimeMachine.Tests;

public class CompanyRepositoryTests
{
    private StockTimeMachineDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<StockTimeMachineDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new StockTimeMachineDbContext(options);
    }

    [Fact]
    public async Task Add_ShouldPersistCompany()
    {
        using var db = CreateDb();
        var repo = new CompanyRepository(db, NullLogger<CompanyRepository>.Instance);

        var company = new Company { Symbol = "TSLA", Name = "Tesla, Inc.", Cik = "0001318605" };
        await repo.Add(company);

        var found = await db.Companies.FindAsync("TSLA");
        Assert.NotNull(found);
        Assert.Equal("Tesla, Inc.", found.Name);
    }

    [Fact]
    public async Task GetBySymbol_ShouldReturnCompany()
    {
        using var db = CreateDb();
        var repo = new CompanyRepository(db, NullLogger<CompanyRepository>.Instance);

        await repo.Add(new Company { Symbol = "AAPL", Name = "Apple Inc.", Cik = "0000320193" });
        var result = await repo.GetBySymbol("aapl");

        Assert.NotNull(result);
        Assert.Equal("AAPL", result.Symbol);
    }

    [Fact]
    public async Task Search_ShouldMatchByName()
    {
        using var db = CreateDb();
        var repo = new CompanyRepository(db, NullLogger<CompanyRepository>.Instance);

        await repo.Add(new Company { Symbol = "MSFT", Name = "Microsoft Corporation", Cik = "0000789019" });
        await repo.Add(new Company { Symbol = "TSLA", Name = "Tesla, Inc.", Cik = "0001318605" });

        var results = await repo.Search("Tesla");
        Assert.Single(results);
        Assert.Equal("TSLA", results[0].Symbol);
    }

    [Fact]
    public async Task Search_ShouldMatchBySymbol()
    {
        using var db = CreateDb();
        var repo = new CompanyRepository(db, NullLogger<CompanyRepository>.Instance);

        await repo.Add(new Company { Symbol = "MSFT", Name = "Microsoft Corporation", Cik = "0000789019" });
        var results = await repo.Search("MSFT");
        Assert.Single(results);
        Assert.Equal("MSFT", results[0].Symbol);
    }

    [Fact]
    public async Task GetBySymbol_ShouldReturnNullForUnknown()
    {
        using var db = CreateDb();
        var repo = new CompanyRepository(db, NullLogger<CompanyRepository>.Instance);

        var result = await repo.GetBySymbol("UNKNOWN");
        Assert.Null(result);
    }

    [Fact]
    public async Task Add_DuplicateSymbol_ShouldThrow()
    {
        using var db = CreateDb();
        var repo = new CompanyRepository(db, NullLogger<CompanyRepository>.Instance);

        await repo.Add(new Company { Symbol = "TSLA", Name = "Tesla Old", Cik = "001" });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repo.Add(new Company { Symbol = "TSLA", Name = "Tesla New", Cik = "002" }));
    }
}

public class HistoricalDataRepositoryTests
{
    private StockTimeMachineDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<StockTimeMachineDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new StockTimeMachineDbContext(options);
    }

    [Fact]
    public async Task StoreFilings_ShouldNotCreateDuplicates()
    {
        using var db = CreateDb();
        var repo = new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance);

        var filings = new List<SecFiling>
        {
            new() { AccessionNumber = "001", FormType = "10-K", FiledAt = new DateTime(2020, 1, 10, 0, 0, 0, DateTimeKind.Utc) }
        };

        await repo.StoreFilings("TSLA", filings);
        await repo.StoreFilings("TSLA", filings);

        Assert.Equal(1, await db.SecFilings.CountAsync());
    }

    [Fact]
    public async Task StorePrices_ShouldNotCreateDuplicates()
    {
        using var db = CreateDb();
        var repo = new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance);

        var prices = new List<PricePoint>
        {
            new() { CompanySymbol = "TSLA", Date = new DateOnly(2020, 1, 15), Close = 100m }
        };

        await repo.StorePrices("TSLA", prices);
        await repo.StorePrices("TSLA", prices);

        Assert.Equal(1, await db.PricePoints.CountAsync());
    }

    [Fact]
    public async Task GetFilingsAsOf_ShouldFilterByDate()
    {
        using var db = CreateDb();
        var repo = new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance);

        await repo.StoreFilings("TSLA", new List<SecFiling>
        {
            new() { AccessionNumber = "001", FormType = "10-K", FiledAt = new DateTime(2020, 1, 10, 0, 0, 0, DateTimeKind.Utc) },
            new() { AccessionNumber = "002", FormType = "10-Q", FiledAt = new DateTime(2020, 1, 20, 0, 0, 0, DateTimeKind.Utc) }
        });

        var result = await repo.GetFilingsAsOf("TSLA", new DateOnly(2020, 1, 15));
        Assert.Single(result);
        Assert.Equal("10-K", result[0].FormType);
    }

    [Fact]
    public async Task GetPricesAsOf_ShouldFilterByDate()
    {
        using var db = CreateDb();
        var repo = new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance);

        await repo.StorePrices("TSLA", new List<PricePoint>
        {
            new() { CompanySymbol = "TSLA", Date = new DateOnly(2020, 1, 14), Close = 99m },
            new() { CompanySymbol = "TSLA", Date = new DateOnly(2020, 1, 15), Close = 100m },
            new() { CompanySymbol = "TSLA", Date = new DateOnly(2020, 1, 16), Close = 101m }
        });

        var result = await repo.GetPricesAsOf("TSLA", new DateOnly(2020, 1, 15));
        Assert.Equal(2, result.Count);
        Assert.All(result, p => Assert.True(p.Date <= new DateOnly(2020, 1, 15)));
    }

    [Fact]
    public async Task GetNewsAsOf_SourceFiltered_BypassesOtherSourceBurst()
    {
        using var db = CreateDb();
        var repo = new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance);

        // 55 newest Alpha Vantage rows push 3 older GDELT rows out of the
        // legacy top-50 read; the source-filtered read must still find them.
        var articles = new List<NewsArticle>();
        for (int i = 0; i < 3; i++)
            articles.Add(new NewsArticle
            {
                Id = $"g{i}", Title = $"G {i}", Source = "GDELT Cloud",
                PublishedAt = new DateTime(2020, 1, 5 + i, 0, 0, 0, DateTimeKind.Utc),
                Url = $"https://example.com/g{i}", CompanySymbol = "TSLA",
            });
        for (int i = 0; i < 55; i++)
            articles.Add(new NewsArticle
            {
                Id = $"a{i}", Title = $"A {i}", Source = "Alpha Vantage",
                PublishedAt = new DateTime(2020, 2, 1 + (i % 27), 0, 0, 0, DateTimeKind.Utc),
                Url = $"https://example.com/a{i}", CompanySymbol = "TSLA",
            });
        await repo.StoreNews("TSLA", articles);

        var legacy = await repo.GetNewsAsOf("TSLA", new DateOnly(2020, 3, 1));
        Assert.Equal(58, legacy.Count);

        var filtered = await repo.GetNewsAsOf("TSLA", new DateOnly(2020, 3, 1), NewsSources.Gdelt);
        Assert.Equal(3, filtered.Count);
        Assert.All(filtered, n => Assert.Contains("GDELT", n.Source));
    }

    [Fact]
    public async Task StoreNews_PersistsSentimentScores()
    {
        using var db = CreateDb();
        var repo = new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance);

        await repo.StoreNews("TSLA", new List<NewsArticle>
        {
            new() { Id = "s1", Title = "Scored", Source = "MarketAux", PublishedAt = new DateTime(2020, 1, 10, 0, 0, 0, DateTimeKind.Utc), Url = "https://example.com/s1", CompanySymbol = "TSLA", SentimentScore = 0.42m },
            new() { Id = "s2", Title = "Unscored", Source = "MarketAux", PublishedAt = new DateTime(2020, 1, 11, 0, 0, 0, DateTimeKind.Utc), Url = "https://example.com/s2", CompanySymbol = "TSLA", SentimentScore = null },
        });

        var rows = await repo.GetNewsAsOf("TSLA", new DateOnly(2020, 1, 15), NewsSources.MarketAux);
        Assert.Equal(2, rows.Count);
        Assert.Equal(0.42m, rows.First(n => n.Id == "s1").SentimentScore);
        Assert.Null(rows.First(n => n.Id == "s2").SentimentScore);
    }

    [Fact]
    public async Task StoreNews_SkipsGloballyDuplicateIds()
    {
        using var db = CreateDb();
        var repo = new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance);

        // Same URL fetched under two symbols: global content identity wins,
        // first fetch keeps ownership — and the batch must NOT explode.
        await repo.StoreNews("MSFT", new List<NewsArticle>
        {
            new() { Id = "shared", Title = "Shared", Source = "GDELT Cloud", PublishedAt = new DateTime(2020, 1, 10), Url = "https://example.com/shared", CompanySymbol = "MSFT" },
        });
        await repo.StoreNews("AMZN", new List<NewsArticle>
        {
            new() { Id = "shared", Title = "Shared", Source = "GDELT Cloud", PublishedAt = new DateTime(2020, 1, 10), Url = "https://example.com/shared", CompanySymbol = "AMZN" },
            new() { Id = "fresh", Title = "Fresh", Source = "GDELT Cloud", PublishedAt = new DateTime(2020, 1, 11), Url = "https://example.com/fresh", CompanySymbol = "AMZN" },
        });

        Assert.Equal(2, await db.NewsArticles.CountAsync());
        var amzn = await repo.GetNewsAsOf("AMZN", new DateOnly(2020, 1, 15), NewsSources.Gdelt);
        Assert.Equal("fresh", Assert.Single(amzn).Id);
    }

    [Fact]
    public async Task GetNewsAsOf_ReadWindow_HoldsBusyWeeks()
    {
        using var db = CreateDb();
        var repo = new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance);

        // 70 same-day rows: the old top-50 read amputated older days for every
        // consumer. The read window must hold them all.
        var articles = Enumerable.Range(0, 70).Select(i => new NewsArticle
        {
            Id = $"g{i}", Title = $"G {i}", Source = "GDELT Cloud",
            PublishedAt = new DateTime(2020, 1, 10).AddHours(i), Url = $"https://example.com/g{i}", CompanySymbol = "TSLA",
        }).ToList();
        await repo.StoreNews("TSLA", articles);

        var rows = await repo.GetNewsAsOf("TSLA", new DateOnly(2020, 1, 15), NewsSources.Gdelt);
        Assert.Equal(70, rows.Count);
    }

    [Fact]
    public async Task GetPricesAfter_ShouldReturnOnlyFuturePrices()
    {
        using var db = CreateDb();
        var repo = new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance);

        await repo.StorePrices("TSLA", new List<PricePoint>
        {
            new() { CompanySymbol = "TSLA", Date = new DateOnly(2020, 1, 14), Close = 99m },
            new() { CompanySymbol = "TSLA", Date = new DateOnly(2020, 1, 15), Close = 100m },
            new() { CompanySymbol = "TSLA", Date = new DateOnly(2020, 1, 16), Close = 101m },
            new() { CompanySymbol = "TSLA", Date = new DateOnly(2020, 1, 20), Close = 105m }
        });

        var result = await repo.GetPricesAfter("TSLA", new DateOnly(2020, 1, 15));
        Assert.Equal(2, result.Count);
        Assert.All(result, p => Assert.True(p.Date > new DateOnly(2020, 1, 15)));
    }

    [Fact]
    public async Task GetPricesAsOf_ShouldReturnEmptyForUnknownSymbol()
    {
        using var db = CreateDb();
        var repo = new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance);

        var result = await repo.GetPricesAsOf("UNKNOWN", new DateOnly(2020, 1, 15));
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetFilingsAsOf_ShouldReturnEmptyForUnknownSymbol()
    {
        using var db = CreateDb();
        var repo = new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance);

        var result = await repo.GetFilingsAsOf("UNKNOWN", new DateOnly(2020, 1, 15));
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPriceRange_ShouldReturnCorrectRange()
    {
        using var db = CreateDb();
        var repo = new HistoricalDataRepository(db, NullLogger<HistoricalDataRepository>.Instance);

        await repo.StorePrices("TSLA", new List<PricePoint>
        {
            new() { CompanySymbol = "TSLA", Date = new DateOnly(2020, 1, 10), Close = 95m },
            new() { CompanySymbol = "TSLA", Date = new DateOnly(2020, 1, 15), Close = 100m },
            new() { CompanySymbol = "TSLA", Date = new DateOnly(2020, 1, 20), Close = 105m }
        });

        var result = await repo.GetPriceRange("TSLA", new DateOnly(2020, 1, 12), new DateOnly(2020, 1, 18));
        Assert.Single(result);
        Assert.Equal(100m, result[0].Close);
    }
}
