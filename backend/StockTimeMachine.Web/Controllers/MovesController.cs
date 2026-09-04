using Microsoft.AspNetCore.Mvc;
using StockTimeMachine;
using StockTimeMachine.Web.Models.Dto;

namespace StockTimeMachine.Web.Controllers;

// Key Moves investigation surface. Deliberately separate from
// TimeMachineApiController: the existing snapshot endpoint and its contract
// are frozen, and moves work must never alter them.
[Route("api/timemachine")]
[ApiController]
public class MovesController : ControllerBase
{
    private readonly IMoveDetectionService _moves;
    private readonly ICompanyDirectory _directory;
    private readonly INewsProviderFactory _newsFactory;

    public MovesController(
        IMoveDetectionService moves,
        ICompanyDirectory directory,
        INewsProviderFactory newsFactory)
    {
        _moves = moves;
        _directory = directory;
        _newsFactory = newsFactory;
    }

    // Last-100-trading-days investigation window: ranked key movements, each
    // with evidence already filtered to that movement's own cutoff.
    [HttpGet("moves")]
    public async Task<ActionResult<MovesResponse>> Moves(
        [FromQuery] string? symbol,
        [FromQuery] string? date,
        [FromQuery] string? newsSource,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new InvalidHistoricalDateException("Symbol is required.");
        if (!DateOnly.TryParse(date, out var parsedDate))
            throw new InvalidHistoricalDateException("Date must be a valid yyyy-MM-dd value.");

        var selectedNewsSource = NewsSources.Normalize(newsSource ?? _newsFactory.DefaultSource);
        var window = await _moves.GetMoves(symbol, parsedDate, selectedNewsSource, ct);

        return Ok(new MovesResponse(
            Company: MapCompany(window.CompanySymbol),
            DecisionDate: window.DecisionDate,
            NewsSource: window.NewsSource,
            Summary: new WindowSummaryDto(
                window.Summary.TradingDays,
                window.Summary.CumulativeReturnPct,
                window.Summary.Volatility,
                window.Summary.MaxDrawdownPct,
                window.Summary.BestDay,
                window.Summary.BestDayReturnPct,
                window.Summary.WorstDay,
                window.Summary.WorstDayReturnPct,
                window.Summary.SufficientHistory),
            KeyMoves: window.KeyMoves.Select(m => new KeyMoveDto(
                m.Date, m.Close, m.DailyReturnPct, m.ZScore, m.VolumeRatio,
                m.FiveDayMomentumPct, m.Score, m.Flags)).ToList(),
            WindowPrices: window.WindowPrices.Select(p => new PricePointDto(p.Date, p.Open, p.High, p.Low, p.Close, p.Volume)).ToList(),
            EvidenceByDate: window.EvidenceByDate.ToDictionary(
                kvp => kvp.Key,
                kvp => new MoveEvidenceDto(
                    kvp.Value.Filings.Select(f => new MoveFilingDto(
                        f.AccessionNumber, f.FormType,
                        DateTime.SpecifyKind(f.FiledAt, DateTimeKind.Utc), f.Url)).ToList(),
                    kvp.Value.News.Select(n => new MoveNewsDto(
                        n.Title, n.Source,
                        DateTime.SpecifyKind(n.PublishedAt, DateTimeKind.Utc), n.Url)).ToList(),
                    kvp.Value.Social.Select(s => new SocialSignalDto(
                        s.Provider, s.Community, s.Title, s.Excerpt, s.Url,
                        DateTime.SpecifyKind(s.CreatedAt, DateTimeKind.Utc),
                        s.Score, s.CommentCount, s.Flair)).ToList(),
                    kvp.Value.Reaction.Select(r => new MarketReactionDto(r.Date, r.Close)).ToList(),
                    kvp.Value.UnavailableLayers))));
    }

    private CompanySummaryDto MapCompany(string symbol)
    {
        if (_directory.TryGet(symbol, out var info) && info is not null)
            return new CompanySummaryDto(info.Symbol, info.Name, info.Cik, info.Exchange, info.Sector);

        return new CompanySummaryDto(symbol.ToUpperInvariant(), symbol.ToUpperInvariant(), "", "", "");
    }
}
