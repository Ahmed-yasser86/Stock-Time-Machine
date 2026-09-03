using Xunit;
using StockTimeMachine;

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
        Assert.Throws<InvalidHistoricalDateException>(() => HistoricalDate.Create(futureDate));
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
        Assert.Throws<InvalidHistoricalDateException>(() => HistoricalDate.Create(futureFromRef, reference));
    }

    [Fact]
    public void Create_PastDateFromReference_ShouldSucceed()
    {
        var reference = new DateOnly(2020, 6, 15);
        var pastDate = new DateOnly(2020, 1, 15);
        var result = HistoricalDate.Create(pastDate, reference);
        Assert.Equal(pastDate, result.Date);
    }

    [Fact]
    public void Create_ExactlyToday_ShouldSucceed()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var result = HistoricalDate.Create(today);
        Assert.Equal(today, result.Date);
    }

    [Fact]
    public void Create_TomorrowFromReference_ShouldThrow()
    {
        var reference = new DateOnly(2020, 6, 15);
        var tomorrow = new DateOnly(2020, 6, 16);
        Assert.Throws<InvalidHistoricalDateException>(() => HistoricalDate.Create(tomorrow, reference));
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

public class TemporalBoundaryTests
{
    [Fact]
    public void GetCutoffUtc_ShouldReturnEndOfTradingDayInUtc()
    {
        var date = new DateOnly(2020, 1, 15);
        var cutoff = TemporalBoundary.GetCutoffUtc(date);

        Assert.Equal(DateTimeKind.Utc, cutoff.Kind);
        // 23:59:59 ET (UTC-5) = 04:59:59 UTC next day
        Assert.Equal(2020, cutoff.Year);
        Assert.Equal(1, cutoff.Month);
        Assert.Equal(16, cutoff.Day);
        Assert.Equal(4, cutoff.Hour);
        Assert.Equal(59, cutoff.Minute);
        Assert.Equal(59, cutoff.Second);
    }

    [Fact]
    public void GetCutoffUtc_IsTimezoneAware()
    {
        var summerDate = new DateOnly(2020, 7, 15); // EDT (UTC-4)
        var winterDate = new DateOnly(2020, 1, 15); // EST (UTC-5)

        var summerCutoff = TemporalBoundary.GetCutoffUtc(summerDate);
        var winterCutoff = TemporalBoundary.GetCutoffUtc(winterDate);

        // Summer (EDT UTC-4): 23:59:59 ET = 03:59:59 UTC
        // Winter (EST UTC-5): 23:59:59 ET = 04:59:59 UTC
        Assert.Equal(3, summerCutoff.Hour);
        Assert.Equal(4, winterCutoff.Hour);
    }

    [Fact]
    public void GetCutoffUtc_FilingBeforeCutoff_ShouldBeIncluded()
    {
        var cutoff = TemporalBoundary.GetCutoffUtc(new DateOnly(2020, 1, 15));
        var filingBefore = new DateTime(2020, 1, 15, 16, 0, 0, DateTimeKind.Utc); // 4pm UTC, before 4:59:59 UTC cutoff

        Assert.True(filingBefore <= cutoff);
    }

    [Fact]
    public void GetCutoffUtc_FilingAfterCutoff_ShouldBeExcluded()
    {
        var cutoff = TemporalBoundary.GetCutoffUtc(new DateOnly(2020, 1, 15));
        var filingAfter = new DateTime(2020, 1, 16, 5, 0, 0, DateTimeKind.Utc); // after 04:59:59 UTC cutoff

        Assert.False(filingAfter <= cutoff);
    }

    [Fact]
    public void GetCutoffUtc_SameRequestProducesDeterministicResult()
    {
        var date = new DateOnly(2020, 1, 15);
        var first = TemporalBoundary.GetCutoffUtc(date);
        var second = TemporalBoundary.GetCutoffUtc(date);

        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData(2020, 3, 8)]
    [InlineData(2020, 11, 1)]
    [InlineData(2021, 3, 14)]
    [InlineData(2021, 11, 7)]
    [InlineData(2023, 3, 12)]
    [InlineData(2023, 11, 5)]
    public void GetCutoffUtc_DstTransitionDates_ShouldHandleCorrectly(int year, int month, int day)
    {
        var date = new DateOnly(year, month, day);
        var cutoff = TemporalBoundary.GetCutoffUtc(date);
        Assert.Equal(DateTimeKind.Utc, cutoff.Kind);
        Assert.True(cutoff.Hour >= 3 && cutoff.Hour <= 5);
    }

    [Theory]
    [InlineData(2020, 1, 1)]
    [InlineData(2020, 6, 15)]
    [InlineData(2020, 12, 31)]
    [InlineData(2024, 2, 29)]
    public void GetCutoffUtc_VariousDates_ShouldAlwaysBeUtc(int year, int month, int day)
    {
        var date = new DateOnly(year, month, day);
        var cutoff = TemporalBoundary.GetCutoffUtc(date);
        Assert.Equal(DateTimeKind.Utc, cutoff.Kind);
        Assert.Equal(59, cutoff.Second);
    }

    [Fact]
    public void GetCutoffUtc_ConsistentAcrossMultipleCalls()
    {
        var date = new DateOnly(2020, 7, 4);
        var results = Enumerable.Range(0, 100)
            .Select(_ => TemporalBoundary.GetCutoffUtc(date))
            .ToList();
        Assert.All(results, r => Assert.Equal(results[0], r));
    }

    [Theory]
    [InlineData(2020, 2, 29)]
    [InlineData(2024, 2, 29)]
    [InlineData(2024, 12, 31)]
    [InlineData(2025, 7, 4)]
    public void GetCutoffUtc_LeapYearAndYearBoundaries_ValidUtc(int year, int month, int day)
    {
        var date = new DateOnly(year, month, day);
        var cutoff = TemporalBoundary.GetCutoffUtc(date);
        Assert.Equal(DateTimeKind.Utc, cutoff.Kind);
        Assert.Equal(59, cutoff.Second);
    }

    [Fact]
    public void HistoricalDate_Create_Today_AllowsCurrentDay()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var d = HistoricalDate.Create(today);
        Assert.Equal(today, d.Date);
    }

    [Fact]
    public void HistoricalDate_Create_LeapDay_Allowed()
    {
        var leap = new DateOnly(2024, 2, 29);
        var d = HistoricalDate.Create(leap, new DateOnly(2025, 1, 1));
        Assert.Equal(leap, d.Date);
    }
}
