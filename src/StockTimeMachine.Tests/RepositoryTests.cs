using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using StockTimeMachine.Entities;
using StockTimeMachine.Repositories;
using StockTimeMachine.RepositoryContracts;

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
}
