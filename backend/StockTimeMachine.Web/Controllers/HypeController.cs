using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StockTimeMachine;
using StockTimeMachine.Web.Models.Dto;

namespace StockTimeMachine.Web.Controllers;

// Hype-cycle signal surface. Read-only over the moves + narratives pipeline
// and the HypeCases registry: detection is deterministic (HypeSignals),
// never predictive, never causal. Separate controller so existing contracts
// stay frozen.
[Route("api/timemachine/hype")]
[ApiController]
public class HypeController : ControllerBase
{
    // Cap on registry rows scanned per supporting-case search: signal mining
    // over recent cases, not a full-table sweep.
    private const int LibraryScanTake = 200;

    private readonly IMoveDetectionService _moves;
    private readonly INarrativeService _narratives;
    private readonly IHypeCaseStore _cases;
    private readonly IHypeResemblanceService _resemblance;
    private readonly IHypeBriefService _briefs;
    private readonly ICompanyDirectory _directory;
    private readonly INewsProviderFactory _newsFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<HypeController> _logger;

    public HypeController(
        IMoveDetectionService moves,
        INarrativeService narratives,
        IHypeCaseStore cases,
        IHypeResemblanceService resemblance,
        IHypeBriefService briefs,
        ICompanyDirectory directory,
        INewsProviderFactory newsFactory,
        IConfiguration config,
        ILogger<HypeController> logger)
    {
        _moves = moves;
        _narratives = narratives;
        _cases = cases;
        _resemblance = resemblance;
        _briefs = briefs;
        _directory = directory;
        _newsFactory = newsFactory;
        _config = config;
        _logger = logger;
    }

    public sealed record HarvestRequest(string? Symbol, string? Date, string? NewsSource, int? TopMoves);
    public sealed record HarvestResponse(string Symbol, DateOnly AsOfDate, int CasesSaved);

    // Offline case harvesting (Step 7): runs the pipeline with a top-N move
    // override and freezes every move as a HypeCase. Operator opt-in ONLY
    // (Hype:HarvestEnabled=true, i.e. Hype__HarvestEnabled env): disabled by
    // default so the product top-5 default cannot change. The harvest script
    // is the only caller; no product UI touches this route.
    [HttpPost("harvest")]
    public async Task<ActionResult<HarvestResponse>> Harvest([FromBody] HarvestRequest req, CancellationToken ct)
    {
        if (!string.Equals(_config["Hype:HarvestEnabled"], "true", StringComparison.OrdinalIgnoreCase))
            return NotFound();
        if (req is null) throw new InvalidHistoricalDateException("Request body required.");
        if (string.IsNullOrWhiteSpace(req.Symbol))
            throw new InvalidHistoricalDateException("Symbol is required.");
        if (!DateOnly.TryParse(req.Date, out var parsedDate))
            throw new InvalidHistoricalDateException("Date must be a valid yyyy-MM-dd value.");
        HistoricalDate.Create(parsedDate);

        var selectedNewsSource = NewsSources.Normalize(req.NewsSource ?? _newsFactory.DefaultSource);
        var window = await _moves.GetMoves(req.Symbol, parsedDate, selectedNewsSource, ct, progress: null, topMoves: req.TopMoves);
        var topics = await _narratives.GetTopics(req.Symbol, parsedDate, selectedNewsSource, ct);
        var saved = 0;
        foreach (var move in window.KeyMoves)
        {
            await _cases.SaveAsync(HypeCaseProjection.Build(window, move, topics), ct);
            saved++;
        }
        _logger.LogInformation("Hype harvest for {Symbol} on {Date}: {Saved} cases", window.CompanySymbol, parsedDate, saved);
        return Ok(new HarvestResponse(window.CompanySymbol, parsedDate, saved));
    }

    // Signal detections for every key move in the window: current-case
    // matches plus, per signal, the historical registry cases where the same
    // deterministic trigger fired. Corrupt registry rows are skipped loudly.
    [HttpGet("signals")]
    public async Task<ActionResult<HypeSignalsResponse>> Signals(
        [FromQuery] string? symbol,
        [FromQuery] string? date,
        [FromQuery] string? newsSource,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new InvalidHistoricalDateException("Symbol is required.");
        if (!DateOnly.TryParse(date, out var parsedDate))
            throw new InvalidHistoricalDateException("Date must be a valid yyyy-MM-dd value.");
        HistoricalDate.Create(parsedDate);

        var selectedNewsSource = NewsSources.Normalize(newsSource ?? _newsFactory.DefaultSource);
        var window = await _moves.GetMoves(symbol, parsedDate, selectedNewsSource, ct);
        var topics = await _narratives.GetTopics(symbol, parsedDate, selectedNewsSource, ct);

        var library = await _cases.ListRecentAsync(LibraryScanTake, ct);
        var peaks = new List<HypePeakDto>();
        foreach (var move in window.KeyMoves)
        {
            var current = HypeCaseProjection.Build(window, move, topics);
            var detail = HypeCaseLibrary.TryReadDetail(current);
            var signals = new List<HypeSignalDto>();
            if (detail is not null)
            {
                foreach (var match in HypeSignals.Evaluate(detail))
                {
                    var supporting = FindSupportingCases(library, match.SignalId, current.Id);
                    var supporters = supporting
                        .Select(s => new HypeCaseRefDto(
                            s.Row.Id, s.Row.CompanySymbol, s.Row.PeakDate,
                            s.Row.FlagsCsv.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList(),
                            s.Row.Completeness))
                        .ToList();
                    // Realized aftermath only (Step 6): post-peak closes frozen
                    // in each supporting case. Displayed collapsed + disclaimed.
                    var followed = supporting
                        .Select(s => new HypeFollowedCaseDto(
                            s.Row.Id, s.Row.CompanySymbol, s.Row.PeakDate,
                            s.Detail.Reaction
                                .Select(r => new HypeReactionDto(r.Date, r.Close))
                                .ToList()))
                        .ToList();
                    var def = HypeSignalCatalog.ById(match.SignalId);
                    // Resemblance is a recall aid, never a trigger: failures
                    // degrade to no resemblance, never to missing signals.
                    IReadOnlyList<HypeResemblanceDto> resemblance = Array.Empty<HypeResemblanceDto>();
                    try
                    {
                        resemblance = (await _resemblance.FindResemblingAsync(
                                detail, match.TriggerThreadIds, library, ct))
                            .Select(r => new HypeResemblanceDto(
                                r.CaseId, r.Symbol, r.PeakDate, r.Similarity))
                            .ToList();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Resemblance failed for {Signal} on {Case}; continuing without",
                            match.SignalId, current.Id);
                    }
                    signals.Add(new HypeSignalDto(
                        match.SignalId,
                        match.Name,
                        def?.Trigger ?? "",
                        match.TriggerEvidence,
                        supporters,
                        resemblance,
                        followed));
                }
            }
            peaks.Add(new HypePeakDto(
                move.Date,
                move.DailyReturnPct,
                move.Score,
                move.Flags.ToList(),
                current.Completeness,
                signals));
        }

        return Ok(new HypeSignalsResponse(
            MapCompany(window.CompanySymbol),
            parsedDate,
            selectedNewsSource,
            library.Count,
            peaks));
    }

    // Opt-in analyst summary for one detected signal: grounded prose over the
    // triggering threads, same containment family as thread briefs. Never
    // auto-fetched (costs a generation call); null when AI is off or the
    // model declines. Narrates only — the trigger already fired.
    [HttpPost("brief")]
    public async Task<ActionResult<HypeBriefResponse>> Brief([FromBody] HypeBriefRequest req, CancellationToken ct)
    {
        if (req is null) throw new InvalidHistoricalDateException("Request body required.");
        if (string.IsNullOrWhiteSpace(req.Symbol))
            throw new InvalidHistoricalDateException("Symbol is required.");
        if (!DateOnly.TryParse(req.Date, out var parsedDate))
            throw new InvalidHistoricalDateException("Date must be a valid yyyy-MM-dd value.");
        if (!DateOnly.TryParse(req.PeakDate, out var peakDate))
            throw new InvalidHistoricalDateException("PeakDate must be a valid yyyy-MM-dd value.");
        if (string.IsNullOrWhiteSpace(req.SignalId))
            throw new InvalidHistoricalDateException("SignalId is required.");
        HistoricalDate.Create(parsedDate);

        var selectedNewsSource = NewsSources.Normalize(req.NewsSource ?? _newsFactory.DefaultSource);
        var window = await _moves.GetMoves(req.Symbol, parsedDate, selectedNewsSource, ct);
        var move = window.KeyMoves.FirstOrDefault(m => m.Date == peakDate)
            ?? throw new HistoricalDataNotFoundException($"No peak {peakDate:yyyy-MM-dd} in this window.");
        var topics = await _narratives.GetTopics(req.Symbol, parsedDate, selectedNewsSource, ct);
        var current = HypeCaseProjection.Build(window, move, topics);
        var detail = HypeCaseLibrary.TryReadDetail(current)
            ?? throw new HistoricalDataNotFoundException("Peak case could not be read.");
        var match = HypeSignals.Evaluate(detail).FirstOrDefault(m => m.SignalId == req.SignalId);
        if (match is null)
            return Ok(new HypeBriefResponse(null));
        var brief = await _briefs.BriefSignalAsync(window.CompanySymbol, parsedDate, match, detail, ct);
        return Ok(new HypeBriefResponse(
            brief is null ? null : new ClusterBriefDto(brief.Summary, brief.KeyPoints, brief.Model)));
    }

    // Registry cases where the same signal trigger fired, excluding the
    // current case itself, each paired with its readable detail (single
    // evaluation pass feeds both supporter refs and the realized-aftermath
    // panel). Unreadable rows are skipped (never fatal: mining degrades to
    // fewer supporters, logged loudly).
    private List<(HypeCase Row, HypeCaseDetail Detail)> FindSupportingCases(
        IReadOnlyList<HypeCase> library, string signalId, string excludeId)
    {
        var supporters = new List<(HypeCase, HypeCaseDetail)>();
        foreach (var row in library)
        {
            if (row.Id == excludeId)
                continue;
            var detail = HypeCaseLibrary.TryReadDetail(row);
            if (detail is null)
            {
                _logger.LogWarning("Skipping unreadable hype case {Id} in signal search", row.Id);
                continue;
            }
            bool fires;
            try
            {
                fires = HypeSignals.Evaluate(detail).Any(m => m.SignalId == signalId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Signal evaluation failed for hype case {Id}; skipping", row.Id);
                continue;
            }
            if (fires)
                supporters.Add((row, detail));
        }
        return supporters;
    }

    private CompanySummaryDto MapCompany(string symbol)
    {
        if (_directory.TryGet(symbol, out var info) && info is not null)
            return new CompanySummaryDto(info.Symbol, info.Name, info.Cik, info.Exchange, info.Sector);

        return new CompanySummaryDto(symbol.ToUpperInvariant(), symbol.ToUpperInvariant(), "", "", "");
    }
}
