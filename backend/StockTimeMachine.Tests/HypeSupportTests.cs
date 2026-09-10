using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace StockTimeMachine.Tests;

// HypeSupport mining math, extracted from HypeController: supporter search
// (self/future/non-firing/unreadable exclusion) and aftermath aggregation.
public class HypeSupportTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static HypeCaseDetail FiringDetail() => new()
    {
        CompanySymbol = "NFLX",
        PeakDate = new DateOnly(2026, 6, 5),
        PrePeakThreads = new List<HypeCaseThread>
        {
            new() { TopCategory = "FINANCIAL", RepresentativeTitle = "Earnings beat", ArticleIds = new List<string> { "a1", "a2" } },
            new() { TopCategory = "FINANCIAL", RepresentativeTitle = "Revenue records", ArticleIds = new List<string> { "a3", "a4" } },
        },
        RegimePath = new Dictionary<string, string>(),
        Evidence = new HypeCaseEvidence(),
    };

    private static HypeCaseDetail QuietDetail() => new()
    {
        CompanySymbol = "NFLX",
        PeakDate = new DateOnly(2026, 6, 5),
        PrePeakThreads = new List<HypeCaseThread>(),
        RegimePath = new Dictionary<string, string>(),
        Evidence = new HypeCaseEvidence(),
    };

    private static HypeCase Row(string id, HypeCaseDetail? detail, DateOnly peak, string symbol = "NFLX") => new()
    {
        Id = id,
        CompanySymbol = symbol,
        PeakDate = peak,
        CaseJson = detail is null ? "{not json" : JsonSerializer.Serialize(detail, Json),
    };

    [Fact]
    public void FindSupportingCases_ExcludesSelfFutureQuietAndUnreadable()
    {
        var peak = new DateOnly(2026, 6, 5);
        var library = new List<HypeCase>
        {
            Row("SELF", FiringDetail(), peak),
            Row("FUTURE", FiringDetail(), peak.AddDays(1)),
            Row("PAST_FIRING", FiringDetail(), peak.AddDays(-30)),
            Row("PAST_QUIET", QuietDetail(), peak.AddDays(-30)),
            Row("BROKEN", null, peak.AddDays(-30)),
        };

        var supporters = HypeSupport.FindSupportingCases(
            library, "earnings-chatter", "SELF", peak, NullLogger.Instance);

        var single = Assert.Single(supporters);
        Assert.Equal("PAST_FIRING", single.Row.Id);
    }

    [Fact]
    public void FindSupportingCases_SameDayPeakCountsAsSeenBefore()
    {
        var peak = new DateOnly(2026, 6, 5);
        var library = new List<HypeCase>
        {
            Row("SAME_DAY", FiringDetail(), peak),
        };

        var supporters = HypeSupport.FindSupportingCases(
            library, "earnings-chatter", "OTHER", peak, NullLogger.Instance);

        Assert.Single(supporters);
    }

    private static (HypeCase Row, HypeCaseDetail Detail) WithReaction(decimal first, decimal last)
    {
        var detail = QuietDetail();
        detail.Reaction = new List<HypeCaseReaction>
        {
            new() { Date = new DateOnly(2026, 6, 6), Close = first },
            new() { Date = new DateOnly(2026, 6, 9), Close = last },
        };
        var row = Row("X", detail, new DateOnly(2026, 6, 5));
        return (row, detail);
    }

    [Fact]
    public void SummarizeFollowed_AggregatesMedianHighLow()
    {
        var supporting = new List<(HypeCase, HypeCaseDetail)>
        {
            WithReaction(100m, 110m), // +10%
            WithReaction(100m, 120m), // +20%
            WithReaction(200m, 190m), // -5%
        };

        var summary = HypeSupport.SummarizeFollowed(supporting);

        Assert.Equal(3, summary.CasesWithReaction);
        Assert.Equal(10m, summary.MedianMovePct);
        Assert.Equal(20m, summary.ObservedHighPct);
        Assert.Equal(-5m, summary.ObservedLowPct);
    }

    [Fact]
    public void SummarizeFollowed_EvenCount_MediansMiddlePair()
    {
        var supporting = new List<(HypeCase, HypeCaseDetail)>
        {
            WithReaction(100m, 110m), // +10%
            WithReaction(100m, 120m), // +20%
        };

        var summary = HypeSupport.SummarizeFollowed(supporting);

        Assert.Equal(2, summary.CasesWithReaction);
        Assert.Equal(15m, summary.MedianMovePct);
    }

    [Fact]
    public void SummarizeFollowed_NoReactionData_ReturnsNulls()
    {
        var detail = QuietDetail();
        var supporting = new List<(HypeCase, HypeCaseDetail)>
        {
            (Row("X", detail, new DateOnly(2026, 6, 5)), detail),
        };

        var summary = HypeSupport.SummarizeFollowed(supporting);

        Assert.Equal(0, summary.CasesWithReaction);
        Assert.Null(summary.MedianMovePct);
        Assert.Null(summary.ObservedHighPct);
        Assert.Null(summary.ObservedLowPct);
    }
}
