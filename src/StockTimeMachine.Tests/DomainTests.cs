using Xunit;
using StockTimeMachine.Entities;

namespace StockTimeMachine.Tests;

public class HistoricalDateTests
{
    [Fact]
    public void Create_PastDate_ShouldSucceed()
    {
        var date = new DateOnly(2020, 1, 15);
        var result = HistoricalDate.Create(date);
        Assert.Equal(date, result.Date);
    }

    [Fact]
    public void Create_FutureDate_ShouldThrow()
    {
        var futureDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
        Assert.Throws<ArgumentException>(() => HistoricalDate.Create(futureDate));
    }

    [Fact]
    public void Create_Today_ShouldSucceed()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var result = HistoricalDate.Create(today);
        Assert.Equal(today, result.Date);
    }

    [Fact]
    public void Create_WithReferenceDate_ShouldUseReference()
    {
        var reference = new DateOnly(2020, 6, 15);
        var futureFromRef = new DateOnly(2020, 12, 25);
        Assert.Throws<ArgumentException>(() => HistoricalDate.Create(futureFromRef, reference));
    }
}

public class CompanyTests
{
    [Fact]
    public void Company_ShouldHaveRequiredFields()
    {
        var company = new Company
        {
            Symbol = "TSLA",
            Name = "Tesla, Inc.",
            Cik = "0001318605",
            Exchange = "NASDAQ",
            Sector = "Consumer Cyclical",
            Industry = "Auto Manufacturers"
        };

        Assert.Equal("TSLA", company.Symbol);
        Assert.Equal("Tesla, Inc.", company.Name);
        Assert.Equal("0001318605", company.Cik);
    }
}

public class SecFilingTests
{
    [Fact]
    public void SecFiling_ShouldHaveFiledAtTimestamp()
    {
        var filing = new SecFiling
        {
            AccessionNumber = "000-0000000-00",
            FormType = "10-K",
            FiledAt = new DateTime(2020, 1, 10, 16, 30, 0, DateTimeKind.Utc),
            CompanySymbol = "TSLA"
        };

        Assert.Equal(DateTimeKind.Utc, filing.FiledAt.Kind);
    }

    [Fact]
    public void SecFiling_FiledAt_ShouldBeUsableForFiltering()
    {
        var cutoff = new DateTime(2020, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var filingBefore = new SecFiling { FiledAt = new DateTime(2020, 1, 10, 0, 0, 0, DateTimeKind.Utc) };
        var filingAfter = new SecFiling { FiledAt = new DateTime(2020, 1, 20, 0, 0, 0, DateTimeKind.Utc) };

        Assert.True(filingBefore.FiledAt.Date <= cutoff);
        Assert.False(filingAfter.FiledAt.Date <= cutoff);
    }
}

public class NewsArticleTests
{
    [Fact]
    public void NewsArticle_ShouldHavePublishedAtTimestamp()
    {
        var article = new NewsArticle
        {
            Id = "news-1",
            Title = "Tesla announces new factory",
            PublishedAt = new DateTime(2020, 1, 14, 9, 0, 0, DateTimeKind.Utc),
            CompanySymbol = "TSLA"
        };

        Assert.Equal(DateTimeKind.Utc, article.PublishedAt.Kind);
    }
}

public class PricePointTests
{
    [Fact]
    public void PricePoint_ShouldStoreOHLCV()
    {
        var price = new PricePoint
        {
            CompanySymbol = "TSLA",
            Date = new DateOnly(2020, 1, 15),
            Open = 100.00m,
            High = 105.00m,
            Low = 99.00m,
            Close = 103.50m,
            Volume = 1000000
        };

        Assert.Equal(100.00m, price.Open);
        Assert.Equal(103.50m, price.Close);
        Assert.Equal(1000000, price.Volume);
    }
}
