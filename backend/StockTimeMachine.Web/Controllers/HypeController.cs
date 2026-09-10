using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using StockTimeMachine;
using StockTimeMachine.Web.Models.Dto;

namespace StockTimeMachine.Web.Controllers;

// Hype-cycle signal surface. Read-only over the moves + narratives pipeline
// and the HypeCases registry: detection is deterministic (HypeSignals),
// never predictive, never causal. Separate controller so existing contracts
// stay frozen. Operator endpoints (harvest/backfill/reindex/case-stats) live
// in HypeOpsController under the same route prefix.
[Route("api/timemachine/hype")]
[ApiController]
public class HypeController : ControllerBase
{
    private readonly IMoveDetectionService _moves;
    private readonly INarrativeService _narratives;
    private readonly IHypeCaseStore _cases;
    private readonly IHypeResemblanceService _resemblance;
    private readonly IHypeBriefService _briefs;
    private readonly IInvestigationJobStore _jobs;
    private readonly ICompanyDirectory _directory;
    private readonly INewsProviderFactory _newsFactory;
    private readonly ILogger<HypeController> _logger;

    public HypeController(
        IMoveDetectionService moves,
        INarrativeService narratives,
        IHypeCaseStore cases,
        IHypeResemblanceService resemblance,
        IHypeBriefService briefs,
        IInvestigationJobStore jobs,
        ICompanyDirectory directory,
        INewsProviderFactory newsFactory,
        ILogger<HypeController> logger)
    {
        _moves = moves;
        _narratives = narratives;
        _cases = cases;
        _resemblance = resemblance;
        _briefs = briefs;
        _jobs = jobs;
        _directory = directory;
        _newsFactory = newsFactory;
        _logger = logger;
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
        symbol = RequestValidation.RequireSymbol(symbol);
        var parsedDate = RequestValidation.RequireDate(date);
        HistoricalDate.Create(parsedDate);

        return Ok(await BuildSignalsAsync(symbol, parsedDate, newsSource, progress: null, ct));
    }

    // Live detection stream (mirrors moves/stream): real stage events
    // (detecting → threads → projecting → matching) then the full signals
    // payload. Same data as the GET. Validation errors are normal 400s;
    // mid-stream failures arrive as an `error` event.
    // (Reason: hype page must narrate progress while it computes instead of
    // staring at one opaque request.)
    [HttpGet("signals/stream")]
    public async Task SignalsStream(
        [FromQuery] string? symbol,
        [FromQuery] string? date,
        [FromQuery] string? newsSource,
        CancellationToken ct)
    {
        symbol = RequestValidation.RequireSymbol(symbol);
        var parsedDate = RequestValidation.RequireDate(date);
        HistoricalDate.Create(parsedDate);

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";

        var progress = SseWriter.StageProgress(Response, ct);

        try
        {
            var response = await BuildSignalsAsync(symbol, parsedDate, newsSource, progress, ct);
            await SseWriter.WriteEventAsync(Response, "signals", response, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hype stream failed for {Symbol} on {Date}", symbol, parsedDate);
            await SseWriter.WriteEventAsync(Response, "error", new { detail = "Something went wrong. Please try again." }, ct);
        }
    }

    // Regime-relativity footnote (Issue 8), shared by every signal payload.
    public const string RegimeRelativityNote =
        "Regime labels are tertiled within each case's own window — 'tense' in different cases is not the same absolute volatility.";

    // Sector sweep cap (Issue 10): N full pipelines per request is expensive;
    // 8 symbols keeps the sweep responsive without degrading single-symbol paths.
    private const int MaxSectorSymbols = 8;

    // Sector sweep (Issue 10): up to 8 symbols under one shared as-of cutoff,
    // each evaluated independently by the same signals pipeline — no pooled
    // verdicts, no cross-symbol scoring or ranking. Per-symbol failures
    // degrade to error rows; one bad symbol never kills the sweep.
    [HttpGet("sector")]
    public async Task<ActionResult<HypeSectorResponse>> Sector(
        [FromQuery] string? symbols,
        [FromQuery] string? date,
        [FromQuery] string? newsSource,
        CancellationToken ct)
    {
        var (parsedSymbols, parsedDate) = ParseSectorRequest(symbols, date);
        var rows = new List<HypeSectorRowDto>();
        foreach (var symbol in parsedSymbols)
        {
            ct.ThrowIfCancellationRequested();
            rows.Add(await BuildSectorRowAsync(symbol, parsedDate, newsSource, progress: null, ct));
        }
        return Ok(new HypeSectorResponse(parsedDate,
            NewsSources.Normalize(newsSource ?? _newsFactory.DefaultSource),
            rows));
    }

    // Streaming sector sweep (same SSE pattern as signals/stream): per-symbol
    // stage events (symbol-prefixed), one `sector` event with the full
    // payload. Validation errors are normal 400s; per-symbol failures arrive
    // as error rows, catastrophic failures as an `error` event.
    [HttpGet("sector/stream")]
    public async Task SectorStream(
        [FromQuery] string? symbols,
        [FromQuery] string? date,
        [FromQuery] string? newsSource,
        CancellationToken ct)
    {
        var (parsedSymbols, parsedDate) = ParseSectorRequest(symbols, date);
        var normalizedSource = NewsSources.Normalize(newsSource ?? _newsFactory.DefaultSource);

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";

        try
        {
            var rows = new List<HypeSectorRowDto>();
            foreach (var symbol in parsedSymbols)
            {
                ct.ThrowIfCancellationRequested();
                var progress = new Progress<SnapshotProgress>(stage =>
                {
                    SseWriter.WriteEventAsync(Response, "stage", new
                    {
                        stage = stage.Stage,
                        state = stage.State,
                        detail = $"{symbol}: {stage.Detail}",
                        count = stage.Count
                    }, ct).GetAwaiter().GetResult();
                });
                rows.Add(await BuildSectorRowAsync(symbol, parsedDate, newsSource, progress, ct));
                await SseWriter.WriteEventAsync(Response, "row", rows[^1], ct);
            }
            await SseWriter.WriteEventAsync(Response, "sector", new HypeSectorResponse(parsedDate, normalizedSource, rows), ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hype sector stream failed for {Symbols} on {Date}", symbols, parsedDate);
            await SseWriter.WriteEventAsync(Response, "error", new { detail = "Something went wrong. Please try again." }, ct);
        }
    }

    // Shared sector validation: 1–8 distinct upper-cased symbols, one valid
    // past date. Throws InvalidHistoricalDateException (400) otherwise —
    // before any pipeline work starts.
    private static (IReadOnlyList<string> Symbols, DateOnly Date) ParseSectorRequest(
        string? symbols, string? date)
    {
        var parsed = (symbols ?? "")
            .Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim().ToUpperInvariant())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (parsed.Count == 0)
            throw new InvalidHistoricalDateException("At least one symbol is required (symbols=AAA,BBB,...).");
        if (parsed.Count > MaxSectorSymbols)
            throw new InvalidHistoricalDateException(
                $"At most {MaxSectorSymbols} symbols per sector sweep (got {parsed.Count}).");
        var parsedDate = RequestValidation.RequireDate(date);
        HistoricalDate.Create(parsedDate);
        return (parsed, parsedDate);
    }

    // One sector row: the full single-symbol pipeline, failure-isolated.
    private async Task<HypeSectorRowDto> BuildSectorRowAsync(
        string symbol, DateOnly parsedDate, string? newsSource,
        IProgress<SnapshotProgress>? progress, CancellationToken ct)
    {
        try
        {
            var response = await BuildSignalsAsync(symbol, parsedDate, newsSource, progress, ct);
            if (response.Peaks.Count == 0)
            {
                // Silent empty rows read as "not investigated". Resolve the
                // reason from data already in hand: unknown symbol (directory
                // miss) vs thin/calm history — no new plumbing required.
                var reason = _directory.TryGet(symbol, out var info) && info is not null
                    ? $"{symbol} returned no key moves for this window — price history too thin or no significant moves. Try another date."
                    : $"{symbol} is not a known symbol — check the ticker. No investigation ran for this row.";
                _logger.LogInformation("Sector row empty for {Symbol}: {Reason}", symbol, reason);
                return new HypeSectorRowDto(symbol, CompanyMapper.Map(_directory, response.Company.Symbol), reason,
                    Array.Empty<HypePeakDto>());
            }
            return new HypeSectorRowDto(symbol, CompanyMapper.Map(_directory, response.Company.Symbol), null, response.Peaks);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sector row failed for {Symbol}; degrading to error row", symbol);
            return new HypeSectorRowDto(symbol, CompanyMapper.Map(_directory, symbol),
                "This symbol could not be evaluated — other rows are unaffected.", Array.Empty<HypePeakDto>());
        }
    }

    private async Task<HypeSignalsResponse> BuildSignalsAsync(
        string symbol, DateOnly parsedDate, string? newsSource,
        IProgress<SnapshotProgress>? progress, CancellationToken ct)
    {
        var selectedNewsSource = NewsSources.Normalize(newsSource ?? _newsFactory.DefaultSource);
        var window = await _moves.GetMoves(symbol, parsedDate, selectedNewsSource, ct, progress);
        var topics = await _narratives.GetTopics(symbol, parsedDate, selectedNewsSource, ct, progress);

        progress?.Report(new SnapshotProgress("projecting", "started",
            $"{window.KeyMoves.Count} peaks into cases"));
        var cases = window.KeyMoves
            .Select(move => HypeCaseProjection.Build(window, move, topics))
            .ToList();
        progress?.Report(new SnapshotProgress("projecting", "complete",
            $"{cases.Count} cases", cases.Count));

        progress?.Report(new SnapshotProgress("matching", "started", "evaluating triggers"));
        // Uncapped library read (reason: live matching must see the whole
        // registry, not an arbitrary most-recent-N).
        var library = await _cases.ListAllAsync(ct);
        progress?.Report(new SnapshotProgress("resembling", "started",
            $"joining against {library.Count} registry cases"));
        var peaks = new List<HypePeakDto>();
        foreach (var (current, move) in cases.Zip(window.KeyMoves, (c, m) => (c, m)))
        {
            var detail = HypeCaseLibrary.TryReadDetail(current);
            var signals = new List<HypeSignalDto>();
            if (detail is not null)
            {
                foreach (var match in HypeSignals.Evaluate(detail))
                {
                    var supporting = HypeSupport.FindSupportingCases(library, match.SignalId, current.Id, move.Date, _logger);
                    var supporters = supporting
                        .Select(s => new HypeCaseRefDto(
                            s.Row.Id, s.Row.CompanySymbol, s.Row.PeakDate,
                            s.Row.FlagsCsv.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList(),
                            s.Row.Completeness, s.Row.NewsSource ?? ""))
                        .ToList();
                    // Realized aftermath only (Step 6 / Issue 4): post-peak
                    // closes frozen in each supporting case, plus the
                    // aggregate first→last move (median/high/low).
                    // Displayed collapsed + disclaimed.
                    var followed = supporting
                        .Select(s => new HypeFollowedCaseDto(
                            s.Row.Id, s.Row.CompanySymbol, s.Row.PeakDate,
                            s.Detail.Reaction
                                .Select(r => new HypeReactionDto(r.Date, r.Close))
                                .ToList(),
                            s.Row.NewsSource ?? ""))
                        .ToList();
                    var followedSummary = MapFollowedSummary(HypeSupport.SummarizeFollowed(supporting));
                    var def = HypeSignalCatalog.ById(match.SignalId);
                    // Resemblance is a recall aid, never a trigger: failures
                    // degrade to no resemblance, never to missing signals.
                    IReadOnlyList<HypeResemblanceDto> resemblance = Array.Empty<HypeResemblanceDto>();
                    try
                    {
                        resemblance = (await _resemblance.FindResemblingAsync(
                                detail, match.TriggerThreadIds, library, ct))
                            .Select(r => new HypeResemblanceDto(
                                r.CaseId, r.Symbol, r.PeakDate, r.Similarity, r.Kind, r.NewsSource ?? ""))
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
                        match.TriggerEvidence
                            .Select(e => new TriggerEvidenceItemDto(
                                e.RenderedText, e.ThreadSize, e.RelevanceRate,
                                e.Category ?? "", e.CategoryBasis ?? "",
                                e.CategoryRationale ?? ""))
                            .ToList(),
                        supporters,
                        resemblance,
                        followed,
                        followedSummary,
                        def?.DirectionalNote ?? "",
                        RegimeRelativityNote));
                }
            }
            // Freeze-then-read label (Issue 4): compare the live detail
            // against the frozen registry row for this same case id.
            // No row yet (fresh investigation) or a match → no label.
            var recomputedNote = "";
            var frozenRow = library.FirstOrDefault(r => r.Id == current.Id);
            if (detail is not null && frozenRow is not null)
            {
                var frozen = HypeCaseLibrary.TryReadDetail(frozenRow);
                if (!HypeCaseProjection.MatchesFrozen(frozen, detail))
                    recomputedNote = "Recomputed now — may differ from frozen registry record.";
            }
            peaks.Add(new HypePeakDto(
                move.Date,
                move.DailyReturnPct,
                move.Score,
                move.Flags.ToList(),
                current.Completeness,
                signals,
                recomputedNote));
        }
        progress?.Report(new SnapshotProgress("matching", "complete",
            $"{peaks.Sum(p => p.Signals.Count)} signals on {peaks.Count} peaks", peaks.Count));

        return new HypeSignalsResponse(
            CompanyMapper.Map(_directory, window.CompanySymbol),
            parsedDate,
            selectedNewsSource,
            library.Count,
            peaks);
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

        // Frozen-row brief path (Issue 4): a resolvable registry case id
        // briefs the frozen detail — reproducible prose over frozen evidence.
        // Unresolvable or unreadable rows fall back to live recomputation
        // below (the request still carries full live context; a 404 here
        // would silently kill briefs for unharvested windows).
        var selectedNewsSource = NewsSources.Normalize(req.NewsSource ?? _newsFactory.DefaultSource);
        var frozenRef = HypeCaseProjection.TryParseCaseId(req.CaseId);
        if (frozenRef is not null)
        {
            var frozenRow = await _cases.GetAsync(frozenRef.Value.Symbol, frozenRef.Value.PeakDate, ct);
            var frozenDetail = frozenRow is null ? null : HypeCaseLibrary.TryReadDetail(frozenRow);
            if (frozenDetail is not null)
            {
                var frozenMatch = HypeSignals.Evaluate(frozenDetail).FirstOrDefault(m => m.SignalId == req.SignalId);
                if (frozenMatch is null)
                    return Ok(new HypeBriefResponse(null));
                var frozenBrief = await _briefs.BriefSignalAsync(
                    frozenDetail.CompanySymbol, parsedDate, frozenMatch, frozenDetail, ct);
                return Ok(new HypeBriefResponse(
                    frozenBrief is null ? null : new ClusterBriefDto(frozenBrief.Summary, frozenBrief.KeyPoints, frozenBrief.Model)));
            }
        }
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

    // Registry cases where the same signal trigger fired: mining math lives
    // in HypeSupport (Application layer, unit-tested); the endpoint only
    // maps the result to DTOs.
    private static HypeFollowedSummaryDto MapFollowedSummary(HypeFollowedSummary summary) =>
        new(summary.CasesWithReaction, summary.MedianMovePct, summary.ObservedHighPct, summary.ObservedLowPct);
}
