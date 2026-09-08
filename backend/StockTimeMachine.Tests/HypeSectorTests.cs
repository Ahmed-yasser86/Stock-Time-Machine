using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using StockTimeMachine;
using StockTimeMachine.Web.Controllers;
using StockTimeMachine.Web.Models.Dto;

namespace StockTimeMachine.Tests;

// Issue 10: sector sweep — shared cutoff, per-symbol independence,
// failure isolation, symbol cap. Controller-level with mocked pipeline.
public class HypeSectorTests
{
    private static readonly DateOnly Date = new(2026, 6, 15);

    private static MovesWindow Window(string symbol, DateOnly peak) => new()
    {
        CompanySymbol = symbol,
        DecisionDate = Date,
        NewsSource = NewsSources.Gdelt,
        Summary = new WindowSummary { TradingDays = 100, SufficientHistory = true },
        KeyMoves = new List<KeyMove> { new() { Date = peak } },
        Regimes = new Dictionary<string, string>(),
        EvidenceByDate = new Dictionary<string, MoveEvidence>
        {
            [peak.ToString("yyyy-MM-dd")] = new MoveEvidence(),
        },
    };

    private static NarrativeTopicsResult Topics(string symbol) => new()
    {
        CompanySymbol = symbol,
        AsOfDate = Date,
        NewsSource = NewsSources.Gdelt,
        Topics = new List<TopicCluster>(),
    };

    private static HypeController Sut(
        Mock<IMoveDetectionService>? moves = null,
        Mock<INarrativeService>? narratives = null,
        Mock<IHypeCaseStore>? cases = null)
    {
        moves ??= new Mock<IMoveDetectionService>();
        narratives ??= new Mock<INarrativeService>();
        cases ??= new Mock<IHypeCaseStore>();
        cases.Setup(c => c.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<HypeCase>());
        return new HypeController(
            moves.Object,
            narratives.Object,
            cases.Object,
            Mock.Of<IHypeResemblanceService>(),
            Mock.Of<IHypeBriefService>(),
            Mock.Of<IHypeCaseIndexer>(),
            Mock.Of<IHypeFilingService>(),
            Mock.Of<IVectorStore>(),
            Mock.Of<IInvestigationJobStore>(),
            Mock.Of<ICompanyDirectory>(),
            Mock.Of<INewsProviderFactory>(),
            Mock.Of<IConfiguration>(),
            NullLogger<HypeController>.Instance);
    }

    [Fact]
    public async Task Sector_ReturnsOneRowPerSymbol_UnderSharedCutoff()
    {
        var moves = new Mock<IMoveDetectionService>();
        moves.Setup(m => m.GetMoves(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>(), It.IsAny<IProgress<SnapshotProgress>?>(), It.IsAny<int?>()))
            .ReturnsAsync((string s, DateOnly d, string? n, CancellationToken _, IProgress<SnapshotProgress>? __, int? ___) =>
                Window(s, new DateOnly(2026, 6, 5)));
        var narratives = new Mock<INarrativeService>();
        narratives.Setup(n => n.GetTopics(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>(), It.IsAny<IProgress<SnapshotProgress>?>()))
            .ReturnsAsync((string s, DateOnly d, string? n, CancellationToken _, IProgress<SnapshotProgress>? __) =>
                Topics(s));
        var sut = Sut(moves, narratives);

        var result = await sut.Sector("NVDA, msft", "2026-06-15", "gdelt", CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<HypeSectorResponse>(ok.Value);

        // One shared cutoff across rows; symbols normalized + deduped.
        Assert.Equal(Date, response.AsOfDate);
        Assert.Equal(NewsSources.Gdelt, response.NewsSource);
        Assert.Equal(2, response.Rows.Count);
        Assert.Equal("NVDA", response.Rows[0].Symbol);
        Assert.Equal("MSFT", response.Rows[1].Symbol);
        Assert.All(response.Rows, r =>
        {
            Assert.Null(r.Error);
            var peak = Assert.Single(r.Peaks);
            Assert.Equal(new DateOnly(2026, 6, 5), peak.PeakDate);
        });
    }

    [Fact]
    public async Task Sector_FailingSymbol_BecomesErrorRow_OthersUnaffected()
    {
        var moves = new Mock<IMoveDetectionService>();
        moves.Setup(m => m.GetMoves("BAD", It.IsAny<DateOnly>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>(), It.IsAny<IProgress<SnapshotProgress>?>(), It.IsAny<int?>()))
            .ThrowsAsync(new InvalidOperationException("provider down"));
        moves.Setup(m => m.GetMoves("NVDA", It.IsAny<DateOnly>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>(), It.IsAny<IProgress<SnapshotProgress>?>(), It.IsAny<int?>()))
            .ReturnsAsync(Window("NVDA", new DateOnly(2026, 6, 5)));
        var narratives = new Mock<INarrativeService>();
        narratives.Setup(n => n.GetTopics(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>(), It.IsAny<IProgress<SnapshotProgress>?>()))
            .ReturnsAsync((string s, DateOnly d, string? n, CancellationToken _, IProgress<SnapshotProgress>? __) =>
                Topics(s));
        var sut = Sut(moves, narratives);

        var result = await sut.Sector("NVDA,BAD", "2026-06-15", "gdelt", CancellationToken.None);
        var response = Assert.IsType<HypeSectorResponse>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(2, response.Rows.Count);
        Assert.Null(response.Rows[0].Error);
        Assert.NotNull(response.Rows[1].Error);
        Assert.Empty(response.Rows[1].Peaks);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Sector_RequiresAtLeastOneSymbol(string? symbols)
    {
        var sut = Sut();

        await Assert.ThrowsAsync<InvalidHistoricalDateException>(() =>
            sut.Sector(symbols, "2026-06-15", "gdelt", CancellationToken.None));
    }

    [Fact]
    public async Task Sector_RejectsMoreThanEightSymbols()
    {
        var sut = Sut();

        var ex = await Assert.ThrowsAsync<InvalidHistoricalDateException>(() =>
            sut.Sector("A,B,C,D,E,F,G,H,I", "2026-06-15", "gdelt", CancellationToken.None));
        Assert.Contains("8", ex.Message);
    }

    [Fact]
    public async Task Sector_RejectsBadDate()
    {
        var sut = Sut();

        await Assert.ThrowsAsync<InvalidHistoricalDateException>(() =>
            sut.Sector("NVDA", "not-a-date", "gdelt", CancellationToken.None));
    }
}
