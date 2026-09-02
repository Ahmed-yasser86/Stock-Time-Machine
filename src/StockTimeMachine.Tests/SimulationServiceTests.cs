using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using StockTimeMachine.Entities;
using StockTimeMachine.Repositories;
using StockTimeMachine.RepositoryContracts;
using StockTimeMachine.Services;

namespace StockTimeMachine.Tests;

public class SimulationServiceTests
{
    private readonly StockTimeMachineDbContext _db;
    private readonly SimulationService _sut;

    public SimulationServiceTests()
    {
        var dbName = $"SimulationTests_{Guid.NewGuid()}";
        _db = new StockTimeMachineDbContext(
            new DbContextOptionsBuilder<StockTimeMachineDbContext>()
                .UseInMemoryDatabase(dbName).Options);

        _sut = new SimulationService(
            new HistoricalDataRepository(_db, NullLogger<HistoricalDataRepository>.Instance),
            NullLogger<SimulationService>.Instance);
    }

    [Fact]
    public async Task Run_ShouldCalculateSharesFromRawPrice()
    {
        var price = new PricePoint
        {
            CompanySymbol = "TSLA",
            Date = new DateOnly(2020, 1, 15),
            Close = 100m, Open = 99m, High = 101m, Low = 98m, Volume = 1000
        };
        await _db.PricePoints.AddAsync(price);
        await _db.SaveChangesAsync();

        var result = await _sut.Run("TSLA", new DateOnly(2020, 1, 15), 10000m);

        Assert.Equal(100m, result.EntryPrice);
        Assert.Equal(100m, result.SharesPurchased); // 10000 / 100 = 100 shares
        Assert.Equal(10000m, result.InvestmentAmount);
    }

    [Fact]
    public async Task Run_ShouldCalculateReturnCorrectlyWithRawPrices()
    {
        var entryPrice = new PricePoint
        {
            CompanySymbol = "TSLA",
            Date = new DateOnly(2020, 1, 15),
            Close = 100m, Open = 99m, High = 101m, Low = 98m, Volume = 1000
        };
        var exitPrice = new PricePoint
        {
            CompanySymbol = "TSLA",
            Date = new DateOnly(2020, 6, 15),
            Close = 150m, Open = 149m, High = 151m, Low = 148m, Volume = 2000
        };
        await _db.PricePoints.AddRangeAsync(entryPrice, exitPrice);
        await _db.SaveChangesAsync();

        var result = await _sut.Run("TSLA", new DateOnly(2020, 1, 15), 10000m, new DateOnly(2020, 6, 15));

        Assert.Equal(100m, result.SharesPurchased);
        Assert.Equal(15000m, result.FinalValue); // 100 * 150
        Assert.Equal(50m, result.ReturnPercentage); // (15000 - 10000) / 10000 * 100
    }

    [Fact]
    public async Task Run_GivenSameInputs_ShouldProduceSameResult()
    {
        var price = new PricePoint
        {
            CompanySymbol = "TSLA",
            Date = new DateOnly(2020, 1, 15),
            Close = 100m, Open = 99m, High = 101m, Low = 98m, Volume = 1000
        };
        await _db.PricePoints.AddAsync(price);
        await _db.SaveChangesAsync();

        var result1 = await _sut.Run("TSLA", new DateOnly(2020, 1, 15), 10000m);
        var result2 = await _sut.Run("TSLA", new DateOnly(2020, 1, 15), 10000m);

        Assert.Equal(result1.FinalValue, result2.FinalValue);
        Assert.Equal(result1.ReturnPercentage, result2.ReturnPercentage);
    }

    [Fact]
    public async Task Run_WhenExitDateMissing_ShouldUseEntryDate()
    {
        var price = new PricePoint
        {
            CompanySymbol = "TSLA",
            Date = new DateOnly(2020, 1, 15),
            Close = 100m, Open = 99m, High = 101m, Low = 98m, Volume = 1000
        };
        await _db.PricePoints.AddAsync(price);
        await _db.SaveChangesAsync();

        var result = await _sut.Run("TSLA", new DateOnly(2020, 1, 15), 10000m);

        Assert.Equal(new DateOnly(2020, 1, 15), result.ExitDate);
        Assert.Equal(result.EntryPrice, result.ExitPrice);
        Assert.Equal(0m, result.ReturnPercentage);
    }

    [Fact]
    public async Task Run_MustUseDecimalArithmetic_NotDouble()
    {
        var price = new PricePoint
        {
            CompanySymbol = "TSLA",
            Date = new DateOnly(2020, 1, 15),
            Close = 33.33m, Open = 33m, High = 34m, Low = 32m, Volume = 1000
        };
        await _db.PricePoints.AddAsync(price);
        await _db.SaveChangesAsync();

        var result = await _sut.Run("TSLA", new DateOnly(2020, 1, 15), 10000m);

        // 10000 / 33.33 = 300.0300... shares (decimal precision)
        Assert.Equal(typeof(decimal), result.SharesPurchased.GetType());
        Assert.True(result.SharesPurchased > 300m);
        Assert.True(result.SharesPurchased < 301m);
    }
}
