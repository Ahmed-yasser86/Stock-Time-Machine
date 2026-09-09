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
    private readonly IMoveDetectionService _moves;
    private readonly INarrativeService _narratives;
    private readonly IHypeCaseStore _cases;
    private readonly IHypeResemblanceService _resemblance;
    private readonly IHypeBriefService _briefs;
    private readonly IHypeCaseIndexer _indexer;
    private readonly IHypeFilingService _filingSummaries;
    private readonly IVectorStore _vectors;
    private readonly IInvestigationJobStore _jobs;
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
        IHypeCaseIndexer indexer,
        IHypeFilingService filingSummaries,
        IVectorStore vectors,
        IInvestigationJobStore jobs,
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
        _indexer = indexer;
        _filingSummaries = filingSummaries;
        _vectors = vectors;
        _jobs = jobs;
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
        var indexed = 0;
        foreach (var move in window.KeyMoves)
        {
            var hypeCase = HypeCaseProjection.Build(window, move, topics);
            await _cases.SaveAsync(hypeCase, ct);
            saved++;
            // Indexing mirrors the runner hook rule: best-effort, never fatal.
            try
            {
                var detail = HypeCaseLibrary.TryReadDetail(hypeCase);
                if (detail is not null)
                    indexed += await _indexer.IndexCaseAsync(detail, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Harvest indexing failed for {Case}; registry row kept", hypeCase.Id);
            }
        }
        // Structured filing summaries, same bound as the runner hook.
        try
        {
            await _filingSummaries.EnsureSummariesAsync(window.CompanySymbol,
                window.EvidenceByDate.Values.SelectMany(e => e.Filings),
                parsedDate, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Harvest filing summaries failed; continuing");
        }
        _logger.LogInformation("Hype harvest for {Symbol} on {Date}: {Saved} cases, {Indexed} vectors",
            window.CompanySymbol, parsedDate, saved, indexed);
        return Ok(new HarvestResponse(window.CompanySymbol, parsedDate, saved));
    }

    public sealed record ReindexResponse(int CasesScanned, int VectorsIndexed);

    public sealed record BackfillRegulatoryResponse(
        int HypeCasesUpdated, int HypeCasesSkipped, int JobsUpdated, int JobsSkipped);

    // Regulatory methodology migration (reg-v1, same operator gate as
    // harvest): recomputes movement-level regulatory evidence under the
    // 30-day window for every frozen HypeCase and every non-running
    // investigation job, from the payloads already stored (no provider
    // calls). Idempotent: clean rows are detected and skipped, so reruns
    // are cheap. Running jobs are never touched.
    [HttpPost("backfill-regulatory")]
    public async Task<ActionResult<BackfillRegulatoryResponse>> BackfillRegulatory(CancellationToken ct)
    {
        if (!string.Equals(_config["Hype:HarvestEnabled"], "true", StringComparison.OrdinalIgnoreCase))
            return NotFound();
        int hypeUpdated = 0, hypeSkipped = 0, jobsUpdated = 0, jobsSkipped = 0;

        foreach (var row in await _cases.ListAllAsync(ct))
        {
            ct.ThrowIfCancellationRequested();
            var detail = HypeCaseLibrary.TryReadDetail(row);
            if (detail is null)
            {
                _logger.LogWarning("Skipping unreadable hype case {Id} in regulatory backfill", row.Id);
                hypeSkipped++;
                continue;
            }
            if (RegulatoryBackfill.IsRegulatoryClean(detail))
            {
                hypeSkipped++;
                continue;
            }
            try
            {
                RegulatoryBackfill.MigrateCaseDetail(detail);
                row.CaseJson = System.Text.Json.JsonSerializer.Serialize(detail, HypeStreamJson);
                await _cases.SaveAsync(row, ct);
                hypeUpdated++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Regulatory backfill failed for hype case {Id}; continuing", row.Id);
                hypeSkipped++;
            }
        }

        foreach (var job in await _jobs.ListAllAsync(ct))
        {
            ct.ThrowIfCancellationRequested();
            if (job.Status == JobStatuses.Running || string.IsNullOrWhiteSpace(job.MovesJson))
            {
                jobsSkipped++;
                continue;
            }
            try
            {
                var window = System.Text.Json.JsonSerializer.Deserialize<MovesWindow>(job.MovesJson, HypeStreamJson);
                if (window is null || !RegulatoryBackfill.MigrateMovesWindow(window))
                {
                    jobsSkipped++;
                    continue;
                }
                if (!await _jobs.UpdateMovesJsonAsync(job.Id,
                    System.Text.Json.JsonSerializer.Serialize(window, HypeStreamJson), ct))
                    jobsSkipped++;
                else
                    jobsUpdated++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Regulatory backfill failed for job {Job}; continuing", job.Id);
                jobsSkipped++;
            }
        }

        _logger.LogInformation(
            "Regulatory backfill (reg-v1): {HypeUpdated} cases + {JobsUpdated} jobs migrated ({HypeSkipped} cases, {JobsSkipped} jobs skipped)",
            hypeUpdated, jobsUpdated, hypeSkipped, jobsSkipped);
        return Ok(new BackfillRegulatoryResponse(hypeUpdated, hypeSkipped, jobsUpdated, jobsSkipped));
    }

    // Migration math lives in RegulatoryBackfill (Application layer,
    // unit-tested); the endpoint above only owns IO. One-shot backfill of
    // cached vectors into the vector store (same
    // operator gate as harvest). Existing rows are never modified: points
    // upsert by stable id.
    [HttpPost("reindex")]
    public async Task<ActionResult<ReindexResponse>> Reindex(CancellationToken ct)
    {
        if (!string.Equals(_config["Hype:HarvestEnabled"], "true", StringComparison.OrdinalIgnoreCase))
            return NotFound();
        // Uncapped library read (reason: reindex must reach every case).
        var library = await _cases.ListAllAsync(ct);
        int scanned = 0, indexed = 0;
        foreach (var row in library)
        {
            ct.ThrowIfCancellationRequested();
            var detail = HypeCaseLibrary.TryReadDetail(row);
            if (detail is null)
            {
                _logger.LogWarning("Skipping unreadable hype case {Id} in reindex", row.Id);
                continue;
            }
            scanned++;
            try
            {
                indexed += await _indexer.IndexCaseAsync(detail, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reindex failed for {Case}; continuing", row.Id);
            }
        }
        _logger.LogInformation("Hype reindex: {Scanned} cases, {Indexed} vectors", scanned, indexed);
        return Ok(new ReindexResponse(scanned, indexed));
    }

    // Pattern-score distribution (operator-only, same gate as harvest):
    // best non-self pattern similarity per registry case → min/max/median/
    // quartiles + 10 buckets over [0,1]. Read-only analytics; decides the
    // pattern threshold with measured data instead of a guess.
    [HttpPost("case-stats")]
    public async Task<ActionResult<HypeCaseStatsResponse>> CaseStats(CancellationToken ct)
    {
        if (!string.Equals(_config["Hype:HarvestEnabled"], "true", StringComparison.OrdinalIgnoreCase))
            return NotFound();
        // Uncapped library read (reason: the distribution must cover every case).
        var library = await _cases.ListAllAsync(ct);
        var bests = new List<double>();
        foreach (var row in library)
        {
            ct.ThrowIfCancellationRequested();
            var detail = HypeCaseLibrary.TryReadDetail(row);
            if (detail is null)
                continue;
            // Same hybrid query the live path uses (structural + content).
            var query = await _resemblance.BuildCaseQueryAsync(detail, ct);
            if (query is null)
                continue;
            IReadOnlyList<VectorHit> hits;
            try
            {
                hits = await _vectors.SearchCasesAsync(query, 10, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Case stats search failed for {Case}; skipping", row.Id);
                continue;
            }
            double? best = null;
            foreach (var hit in hits)
            {
                if (string.Equals(hit.CaseId, row.Id, StringComparison.Ordinal))
                    continue;
                if (string.Equals(hit.Symbol, row.CompanySymbol, StringComparison.OrdinalIgnoreCase) &&
                    Math.Abs(hit.PeakDate.DayNumber - row.PeakDate.DayNumber) <= 30)
                    continue;
                var sim = EmbeddingClustering.Cosine(query, hit.Vector);
                best = best is null ? sim : Math.Max(best.Value, sim);
            }
            if (best.HasValue)
                bests.Add(best.Value);
        }
        bests.Sort();
        double Quantile(double q) => bests.Count == 0 ? 0 :
            bests[Math.Min(bests.Count - 1, (int)(q * bests.Count))];
        var buckets = new int[10];
        foreach (var s in bests)
            buckets[Math.Min(9, (int)(s * 10))]++;
        return Ok(new HypeCaseStatsResponse(
            library.Count, bests.Count,
            bests.Count == 0 ? 0 : Math.Round(bests[0], 3),
            bests.Count == 0 ? 0 : Math.Round(bests[^1], 3),
            Math.Round(Quantile(0.5), 3),
            Math.Round(Quantile(0.25), 3),
            Math.Round(Quantile(0.75), 3),
            buckets.ToList()));
    }

    public sealed record VectorHealthResponse(string Backend, bool Reachable, ulong Points);

    // Vector-store health for operators: which resemblance backend is live
    // and how many points it holds. Never fails the feature either way.
    [HttpGet("vector-health")]
    public async Task<ActionResult<VectorHealthResponse>> VectorHealth(CancellationToken ct)
    {
        var (reachable, points) = await _vectors.HealthAsync(ct);
        return Ok(new VectorHealthResponse(reachable ? "qdrant" : "memory", reachable, points));
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
        if (string.IsNullOrWhiteSpace(symbol))
            throw new InvalidHistoricalDateException("Symbol is required.");
        if (!DateOnly.TryParse(date, out var parsedDate))
            throw new InvalidHistoricalDateException("Date must be a valid yyyy-MM-dd value.");
        HistoricalDate.Create(parsedDate);

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";

        async Task WriteEvent(string name, object payload)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(payload, HypeStreamJson);
            await Response.WriteAsync($"event: {name}\ndata: {json}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }

        var progress = new Progress<SnapshotProgress>(stage =>
        {
            WriteEvent("stage", new
            {
                stage = stage.Stage,
                state = stage.State,
                detail = stage.Detail,
                count = stage.Count
            }).GetAwaiter().GetResult();
        });

        try
        {
            var response = await BuildSignalsAsync(symbol, parsedDate, newsSource, progress, ct);
            await WriteEvent("signals", response);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hype stream failed for {Symbol} on {Date}", symbol, parsedDate);
            await WriteEvent("error", new { detail = "Something went wrong. Please try again." });
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

        async Task WriteEvent(string name, object payload)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(payload, HypeStreamJson);
            await Response.WriteAsync($"event: {name}\ndata: {json}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }

        try
        {
            var rows = new List<HypeSectorRowDto>();
            foreach (var symbol in parsedSymbols)
            {
                ct.ThrowIfCancellationRequested();
                var progress = new Progress<SnapshotProgress>(stage =>
                {
                    WriteEvent("stage", new
                    {
                        stage = stage.Stage,
                        state = stage.State,
                        detail = $"{symbol}: {stage.Detail}",
                        count = stage.Count
                    }).GetAwaiter().GetResult();
                });
                rows.Add(await BuildSectorRowAsync(symbol, parsedDate, newsSource, progress, ct));
                await WriteEvent("row", rows[^1]);
            }
            await WriteEvent("sector", new HypeSectorResponse(parsedDate, normalizedSource, rows));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hype sector stream failed for {Symbols} on {Date}", symbols, parsedDate);
            await WriteEvent("error", new { detail = "Something went wrong. Please try again." });
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
        if (!DateOnly.TryParse(date, out var parsedDate))
            throw new InvalidHistoricalDateException("Date must be a valid yyyy-MM-dd value.");
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
                return new HypeSectorRowDto(symbol, MapCompany(response.Company.Symbol), reason,
                    Array.Empty<HypePeakDto>());
            }
            return new HypeSectorRowDto(symbol, MapCompany(response.Company.Symbol), null, response.Peaks);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sector row failed for {Symbol}; degrading to error row", symbol);
            return new HypeSectorRowDto(symbol, MapCompany(symbol),
                "This symbol could not be evaluated — other rows are unaffected.", Array.Empty<HypePeakDto>());
        }
    }

    private static readonly System.Text.Json.JsonSerializerOptions HypeStreamJson = new(System.Text.Json.JsonSerializerDefaults.Web);

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
                    var supporting = FindSupportingCases(library, match.SignalId, current.Id, move.Date);
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
                    var followedSummary = SummarizeFollowed(supporting);
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
            MapCompany(window.CompanySymbol),
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

    // Registry cases where the same signal trigger fired, excluding the
    // current case itself, each paired with its readable detail (single
    // evaluation pass feeds both supporter refs and the realized-aftermath
    // panel). Unreadable rows are skipped (never fatal: mining degrades to
    // fewer supporters, logged loudly).
    private List<(HypeCase Row, HypeCaseDetail Detail)> FindSupportingCases(
        IReadOnlyList<HypeCase> library, string signalId, string excludeId, DateOnly peakDate)
    {
        var supporters = new List<(HypeCase, HypeCaseDetail)>();
        foreach (var row in library)
        {
            if (row.Id == excludeId)
                continue;
            // No hindsight (Issue 1): a supporter must have peaked no later
            // than the peak it explains — future cases never count as
            // "seen before", even when they sit inside the as-of window.
            if (row.PeakDate > peakDate)
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

    // Aggregate realized move per supporting case (first→last recorded
    // close), then median/high/low across cases with reaction data.
    // Decimal math (money-adjacent); nulls when no case has ≥2 closes.
    private static HypeFollowedSummaryDto SummarizeFollowed(
        List<(HypeCase Row, HypeCaseDetail Detail)> supporting)
    {
        var moves = supporting
            .Select(s => s.Detail.Reaction)
            .Where(r => r.Count >= 2 && r.First().Close != 0)
            .Select(r => (r.Last().Close - r.First().Close) / r.First().Close * 100m)
            .OrderBy(p => p)
            .ToList();
        if (moves.Count == 0)
            return new HypeFollowedSummaryDto(0, null, null, null);
        var mid = moves.Count / 2;
        var median = moves.Count % 2 == 1
            ? moves[mid]
            : (moves[mid - 1] + moves[mid]) / 2m;
        return new HypeFollowedSummaryDto(
            moves.Count,
            Math.Round(median, 2),
            Math.Round(moves.Max(), 2),
            Math.Round(moves.Min(), 2));
    }

    private CompanySummaryDto MapCompany(string symbol)
    {
        if (_directory.TryGet(symbol, out var info) && info is not null)
            return new CompanySummaryDto(info.Symbol, info.Name, info.Cik, info.Exchange, info.Sector);

        return new CompanySummaryDto(symbol.ToUpperInvariant(), symbol.ToUpperInvariant(), "", "", "");
    }
}
