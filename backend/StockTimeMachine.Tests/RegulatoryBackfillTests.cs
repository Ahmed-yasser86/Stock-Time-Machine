using Xunit;
using StockTimeMachine;

namespace StockTimeMachine.Tests;

// reg-v1 migration: frozen payloads recomputed under the 30-day window,
// idempotent, version-stamped. Pure fixtures, no IO.
public class RegulatoryBackfillTests
{
    private static readonly DateOnly Peak = new(2026, 6, 15);

    private static HypeCaseFiling Filing(string acc, string form, DateTime filedAt) => new()
    {
        AccessionNumber = acc,
        FormType = form,
        FiledAt = filedAt,
        Url = "https://sec.gov/" + acc,
    };

    // Legacy frozen case: 2015 history + a day-31 filing + three in-window
    // filings, no version stamp, stale arrival.
    private static HypeCaseDetail LegacyDetail() => new()
    {
        CompanySymbol = "NFLX",
        PeakDate = Peak,
        Evidence = new HypeCaseEvidence
        {
            Filings = new List<HypeCaseFiling>
            {
                Filing("old", "10-K", new DateTime(2015, 7, 20)),
                Filing("edge-out", "8-K", new DateTime(2026, 5, 15)),
                Filing("f1", "8-K", new DateTime(2026, 5, 20)),
                Filing("f2", "8-K", new DateTime(2026, 6, 10)),
                Filing("f3", "8-K", new DateTime(2026, 6, 14)),
            },
            News = new List<HypeCaseNewsItem>
            {
                new() { Title = "Shares jump", PublishedAt = new DateTime(2026, 6, 13) },
            },
            Social = new List<HypeCaseSocialPost>(),
            Arrival = new List<HypeCaseArrival>(),
        },
    };

    [Fact]
    public void MigrateCaseDetail_DropsOutOfWindow_StampsProvenance()
    {
        var detail = LegacyDetail();

        Assert.False(RegulatoryBackfill.IsRegulatoryClean(detail));
        Assert.True(RegulatoryBackfill.MigrateCaseDetail(detail));

        // 2015 history + day-31 filing are gone; in-window trio stays newest-first.
        Assert.Equal(3, detail.Evidence.Filings.Count);
        Assert.Equal("f3", detail.Evidence.Filings[0].AccessionNumber);
        Assert.Equal(3, detail.Evidence.FilingCount);
        Assert.Equal(30, detail.RegulatoryLookbackDays);
        Assert.Equal("reg-v1", detail.RegulatoryMethodology);
        Assert.Equal(1, detail.RegulatoryTiers["very_close"]);
        Assert.Equal(1, detail.RegulatoryTiers["recent"]);
        Assert.Equal(1, detail.RegulatoryTiers["older"]);
        // Arrival rebuilt from windowed evidence: regulatory first-seen is
        // May 20 (not 2015), with window-scoped tier counts in the detail.
        var reg = detail.Evidence.Arrival.Single(a => a.Layer == "regulatory");
        Assert.Equal("observed", reg.State);
        Assert.Equal(new DateTime(2026, 5, 20), reg.FirstSeen);
        Assert.Contains("3 filing(s) within the 30-day window", reg.Detail);
        Assert.Contains("very close: 1, recent: 1, older: 1", reg.Detail);
    }

    [Fact]
    public void MigrateCaseDetail_IsIdempotent()
    {
        var detail = LegacyDetail();

        Assert.True(RegulatoryBackfill.MigrateCaseDetail(detail));
        Assert.True(RegulatoryBackfill.IsRegulatoryClean(detail));
        Assert.False(RegulatoryBackfill.MigrateCaseDetail(detail));
        Assert.True(RegulatoryBackfill.IsRegulatoryClean(detail));
    }

    [Fact]
    public void MigrateMovesWindow_WindowsFilingsPerMove()
    {
        var moveDate = new DateOnly(2026, 6, 9);
        var window = new MovesWindow
        {
            CompanySymbol = "AAPL",
            DecisionDate = moveDate,
            NewsSource = NewsSources.Gdelt,
            KeyMoves = new List<KeyMove>
            {
                new() { Date = moveDate, Score = 0.9 },
            },
        };
        window.EvidenceByDate[moveDate.ToString("yyyy-MM-dd")] = new MoveEvidence
        {
            Filings = new List<SecFiling>
            {
                new() { AccessionNumber = "old", FormType = "10-K",
                    FiledAt = new DateTime(2015, 7, 20), CompanySymbol = "AAPL" },
                new() { AccessionNumber = "f1", FormType = "8-K",
                    FiledAt = new DateTime(2026, 5, 20), CompanySymbol = "AAPL" },
            },
            Arrival = new List<ArrivalEntry>(),
        };

        Assert.True(RegulatoryBackfill.MigrateMovesWindow(window));

        var evidence = window.EvidenceByDate[moveDate.ToString("yyyy-MM-dd")];
        Assert.Single(evidence.Filings);
        Assert.Equal("f1", evidence.Filings[0].AccessionNumber);
        var reg = evidence.Arrival.Single(a => a.Layer == "regulatory");
        Assert.Equal("observed", reg.State);

        // Second run: nothing left to change.
        Assert.False(RegulatoryBackfill.MigrateMovesWindow(window));
    }

    [Fact]
    public void BriefPrompt_CitesFilingPrimaryDocuments()
    {
        var detail = LegacyDetail();
        RegulatoryBackfill.MigrateCaseDetail(detail);
        var match = new HypeSignalMatch
        {
            SignalId = "regulatory-overhang",
            Name = "Regulatory overhang",
            TriggerEvidence = new List<TriggerEvidenceItem> { new() { Text = "t" } },
            TriggerThreadIds = new List<string>(),
        };
        var summaries = new List<HypeFilingSummary>
        {
            new()
            {
                FormType = "8-K",
                FiledAt = new DateTime(2026, 6, 14),
                AccessionNumber = "0001234-26-0001",
                Url = "https://sec.gov/0001234-26-0001",
                Findings = "Item 8.01 disclosed.",
                Disclosures = "None.",
            },
            new()
            {
                FormType = "10-Q",
                FiledAt = new DateTime(2026, 5, 20),
                Url = "https://sec.gov/Archives/xyz",
                Findings = "Revenue up.",
                Disclosures = "Risk factors.",
            },
            new()
            {
                FormType = "8-K",
                FiledAt = new DateTime(2026, 6, 10),
                Findings = "No origin kept.",
                Disclosures = "None.",
            },
        };

        var prompt = HypeBriefPrompt.Build("NFLX", Peak, match, detail,
            new List<HypeBriefArticle>(), filingSummaries: summaries);

        Assert.Contains("[accession 0001234-26-0001]", prompt);
        Assert.Contains("[https://sec.gov/Archives/xyz]", prompt);
        Assert.Contains("[origin untraced in frozen case]", prompt);
    }

    [Fact]
    public void Resemblance_NewsSource_DefaultsUnknown()
    {
        // New provenance field: never assumed, stamped from the registry row
        // by the service (or left "" when the hit has no row).
        Assert.Equal("", new HypeCaseResemblance().NewsSource);
    }
}
