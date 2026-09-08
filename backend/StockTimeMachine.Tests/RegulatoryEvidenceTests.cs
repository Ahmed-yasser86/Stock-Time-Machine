using Xunit;
using StockTimeMachine;

namespace StockTimeMachine.Tests;

// Methodology reg-v1: 30-day candidate window, proximity tiers, first-seen
// and counts window-scoped. Pure and deterministic.
public class RegulatoryEvidenceTests
{
    private static readonly DateOnly Move = new(2026, 6, 9);

    private static SecFiling Filing(int year, int month, int day, string form = "8-K") => new()
    {
        AccessionNumber = $"acc-{year}-{month}-{day}",
        FormType = form,
        FiledAt = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc),
        Url = "https://example.com/f",
        CompanySymbol = "AAPL",
    };

    [Fact]
    public void Window_IsThirtyDaysEndingOnMoveDate()
    {
        var (from, to) = RegulatoryEvidence.Window(Move);

        Assert.Equal(new DateOnly(2026, 5, 10), from);
        Assert.Equal(Move, to);
        Assert.Equal(30, RegulatoryEvidence.LookbackDays);
        Assert.Equal("reg-v1", RegulatoryEvidence.MethodologyVersion);
    }

    [Fact]
    public void Window_AcceptsCustomLookback_ForSensitivityRuns()
    {
        var (from, to) = RegulatoryEvidence.Window(Move, 7);

        Assert.Equal(new DateOnly(2026, 6, 2), from);
        Assert.Equal(Move, to);
    }

    [Theory]
    [InlineData(2026, 6, 9, "very_close")]   // day 0: filed on the move date
    [InlineData(2026, 6, 8, "very_close")]   // day 1
    [InlineData(2026, 6, 7, "recent")]       // day 2
    [InlineData(2026, 6, 2, "recent")]       // day 7
    [InlineData(2026, 6, 1, "older")]        // day 8
    [InlineData(2026, 5, 10, "older")]       // day 30: window edge, inclusive
    public void ClassifyTier_Boundaries(int y, int m, int d, string expected)
    {
        Assert.Equal(expected,
            RegulatoryEvidence.ClassifyTier(new DateTime(y, m, d, 0, 0, 0, DateTimeKind.Utc), Move));
    }

    [Theory]
    [InlineData(2026, 5, 9)]    // day 31: one past the edge
    [InlineData(2015, 7, 20)]   // company history, not movement evidence
    [InlineData(2026, 6, 10)]   // after the move: future knowledge
    public void ClassifyTier_OutOfWindow_ReturnsNull(int y, int m, int d)
    {
        Assert.Null(RegulatoryEvidence.ClassifyTier(
            new DateTime(y, m, d, 0, 0, 0, DateTimeKind.Utc), Move));
    }

    [Fact]
    public void SelectInWindow_KeepsOnlyEligible_NewestFirst()
    {
        var filings = new[]
        {
            Filing(2015, 7, 20),
            Filing(2026, 5, 9),
            Filing(2026, 5, 20),
            Filing(2026, 6, 8),
        };

        var selected = RegulatoryEvidence.SelectInWindow(filings, Move);

        Assert.Equal(2, selected.Count);
        Assert.Equal(new DateTime(2026, 6, 8, 0, 0, 0, DateTimeKind.Utc), selected[0].FiledAt);
        Assert.Equal(new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc), selected[1].FiledAt);
    }

    [Fact]
    public void CountByTier_IgnoresOutOfWindowRows()
    {
        var counts = RegulatoryEvidence.CountByTier(
            new[] { Filing(2015, 7, 20), Filing(2026, 6, 9), Filing(2026, 6, 4), Filing(2026, 5, 20) }, Move);

        Assert.Equal(new RegulatoryEvidence.TierCounts(1, 1, 1), counts);
        Assert.Equal(3, counts.Total);
    }
}
