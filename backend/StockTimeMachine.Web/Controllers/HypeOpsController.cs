using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StockTimeMachine;
using StockTimeMachine.Web.Models.Dto;

namespace StockTimeMachine.Web.Controllers;

// Operator-only hype endpoints: harvest, regulatory backfill, reindex,
// case-stats. Same route prefix as HypeController so URLs are unchanged;
// split out so the product controller stays focused on researcher queries.
// Every mutating/expensive action sits behind the Hype:HarvestEnabled gate
// (operator opt-in; the harvest script is the only caller).
[Route("api/timemachine/hype")]
[ApiController]
public class HypeOpsController : ControllerBase
{
    private readonly IMoveDetectionService _moves;
    private readonly INarrativeService _narratives;
    private readonly IHypeCaseStore _cases;
    private readonly IHypeResemblanceService _resemblance;
    private readonly IHypeCaseIndexer _indexer;
    private readonly IHypeFilingService _filingSummaries;
    private readonly IVectorStore _vectors;
    private readonly IInvestigationJobStore _jobs;
    private readonly INewsProviderFactory _newsFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<HypeOpsController> _logger;

    public HypeOpsController(
        IMoveDetectionService moves,
        INarrativeService narratives,
        IHypeCaseStore cases,
        IHypeResemblanceService resemblance,
        IHypeCaseIndexer indexer,
        IHypeFilingService filingSummaries,
        IVectorStore vectors,
        IInvestigationJobStore jobs,
        INewsProviderFactory newsFactory,
        IConfiguration config,
        ILogger<HypeOpsController> logger)
    {
        _moves = moves;
        _narratives = narratives;
        _cases = cases;
        _resemblance = resemblance;
        _indexer = indexer;
        _filingSummaries = filingSummaries;
        _vectors = vectors;
        _jobs = jobs;
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
        if (!HypeOperatorGate.IsEnabled(_config))
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
        if (!HypeOperatorGate.IsEnabled(_config))
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
        if (!HypeOperatorGate.IsEnabled(_config))
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
        if (!HypeOperatorGate.IsEnabled(_config))
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

    private static readonly System.Text.Json.JsonSerializerOptions HypeStreamJson = new(System.Text.Json.JsonSerializerDefaults.Web);

    public sealed record VectorHealthResponse(string Backend, bool Reachable, ulong Points);

    // Vector-store health for operators: which resemblance backend is live
    // and how many points it holds. Never fails the feature either way.
    [HttpGet("vector-health")]
    public async Task<ActionResult<VectorHealthResponse>> VectorHealth(CancellationToken ct)
    {
        var (reachable, points) = await _vectors.HealthAsync(ct);
        return Ok(new VectorHealthResponse(reachable ? "qdrant" : "memory", reachable, points));
    }
}
