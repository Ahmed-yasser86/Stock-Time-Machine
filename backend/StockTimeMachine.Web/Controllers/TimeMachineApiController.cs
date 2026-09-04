using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using StockTimeMachine;
using StockTimeMachine.Web.Models.Dto;

namespace StockTimeMachine.Web.Controllers;

[Route("api/timemachine")]
[ApiController]
public class TimeMachineApiController : ControllerBase
{
    public const string NewsCoverageDisclaimer =
        "News coverage is best-effort and may be incomplete. Absence of coverage does not mean absence of events.";

    public const string SimulationDisclaimer =
        "This simulation uses raw historical closing prices. Stock splits and dividend payments are not accounted for in this calculation. This is not investment advice.";

    private readonly ITimeMachineService _timeMachine;
    private readonly ISimulationService _simulation;
    private readonly ICompanyDirectory _directory;
    private readonly ICompanyRepository _companyRepo;
    private readonly INewsProviderFactory _newsFactory;
    private readonly IQuoteProvider _quotes;
    private readonly ILogger<TimeMachineApiController> _logger;

    public TimeMachineApiController(
        ITimeMachineService timeMachine,
        ISimulationService simulation,
        ICompanyDirectory directory,
        ICompanyRepository companyRepo,
        INewsProviderFactory newsFactory,
        IQuoteProvider quotes,
        ILogger<TimeMachineApiController> logger)
    {
        _timeMachine = timeMachine;
        _simulation = simulation;
        _directory = directory;
        _companyRepo = companyRepo;
        _newsFactory = newsFactory;
        _quotes = quotes;
        _logger = logger;
    }

    [HttpGet("health")]
    public IActionResult Health() => Ok(new { status = "ok", time = DateTime.UtcNow });

    // Single canonical company search: directory first, then persisted companies,
    // deduplicated by symbol. (Merged from the former CompanySearchController.)
    [HttpGet("company-search")]
    public async Task<ActionResult<IReadOnlyList<CompanyDto>>> CompanySearch([FromQuery] string? q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Ok(Array.Empty<CompanyDto>());

        var query = q.Trim();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<CompanyDto>();

        foreach (var c in _directory.Search(query, 10))
        {
            if (!seen.Add(c.Symbol)) continue;
            results.Add(new CompanyDto(c.Symbol, c.Name, c.Cik, c.Exchange, c.Sector, c.Industry));
        }

        foreach (var c in await _companyRepo.Search(query, ct))
        {
            if (!seen.Add(c.Symbol)) continue;
            results.Add(new CompanyDto(c.Symbol, c.Name ?? "", c.Cik ?? "", c.Exchange ?? "", c.Sector ?? "", c.Industry ?? ""));
            if (results.Count >= 10) break;
        }

        return Ok(results.Take(10).ToList());
    }

    [HttpGet("snapshot")]
    public async Task<ActionResult<SnapshotResponse>> Snapshot(
        [FromQuery] string? symbol,
        [FromQuery] string? date,
        [FromQuery] string? newsSource,
        [FromQuery] string? sections,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new InvalidHistoricalDateException("Symbol is required.");
        if (!DateOnly.TryParse(date, out var parsedDate))
            throw new InvalidHistoricalDateException("Date must be a valid yyyy-MM-dd value.");

        var selectedNewsSource = NewsSources.Normalize(newsSource ?? _newsFactory.DefaultSource);
        // Rescope: comma-separated subset of prices,filings,news,outcome.
        // Omitted or empty means all sections. Unknown keys are a 400.
        var selectedSections = SnapshotSections.Parse(sections);
        var response = await BuildSnapshotResponse(symbol, parsedDate, selectedNewsSource, selectedSections, progress: null, ct);

        return Ok(response);
    }

    // Live reconstruction stream (US-06): Server-Sent Events carrying one honest
    // SnapshotProgress per pipeline stage, then the full snapshot as the final
    // `snapshot` event. Stage states are real (started/complete/failed/skipped) —
    // never decorative. Validation errors are thrown before streaming starts
    // (normal 400s); mid-stream failures arrive as an `error` event.
    [HttpGet("snapshot/stream")]
    public async Task SnapshotStream(
        [FromQuery] string? symbol,
        [FromQuery] string? date,
        [FromQuery] string? newsSource,
        [FromQuery] string? sections,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new InvalidHistoricalDateException("Symbol is required.");
        if (!DateOnly.TryParse(date, out var parsedDate))
            throw new InvalidHistoricalDateException("Date must be a valid yyyy-MM-dd value.");

        var selectedNewsSource = NewsSources.Normalize(newsSource ?? _newsFactory.DefaultSource);
        var selectedSections = SnapshotSections.Parse(sections);

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";

        async Task WriteEvent(string name, object payload)
        {
            var json = JsonSerializer.Serialize(payload, SnapshotStreamJson);
            await Response.WriteAsync($"event: {name}\ndata: {json}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }

        var progress = new Progress<SnapshotProgress>(stage =>
        {
            // Fire-and-forget is wrong here: stages must arrive in order. The
            // service awaits each resolve, so reports are already sequential;
            // block the callback briefly to preserve wire order.
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
            var response = await BuildSnapshotResponse(symbol, parsedDate, selectedNewsSource, selectedSections, progress, ct);
            await WriteEvent("snapshot", response);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Snapshot stream failed for {Symbol} on {Date}", symbol, parsedDate);
            await WriteEvent("error", new { detail = "Something went wrong. Please try again." });
        }
    }

    private static readonly JsonSerializerOptions SnapshotStreamJson = new(JsonSerializerDefaults.Web);

    private async Task<SnapshotResponse> BuildSnapshotResponse(
        string symbol,
        DateOnly parsedDate,
        string selectedNewsSource,
        IReadOnlySet<string>? selectedSections,
        IProgress<SnapshotProgress>? progress,
        CancellationToken ct)
    {
        var snapshot = await _timeMachine.GetSnapshot(symbol, parsedDate, selectedNewsSource, selectedSections, progress, ct);
        var cutoff = TemporalBoundary.GetCutoffUtc(parsedDate);

        // Warnings describe requested sections only: an excluded section is a
        // client choice, not a coverage gap, so it stays silent.
        var warnings = new List<string>();
        if (SnapshotSections.Includes(selectedSections, SnapshotSections.Prices))
        {
            if (!snapshot.HasMarketData)
                warnings.Add($"Historical market data is not available for {snapshot.CompanySymbol} on {parsedDate:MMMM d, yyyy}. Try a nearby date.");
            else if (snapshot.PriceDate.HasValue && snapshot.PriceDate.Value < parsedDate)
                warnings.Add($"Markets were closed on {parsedDate:MMMM d, yyyy}. Showing data as of the previous trading day, {snapshot.PriceDate.Value:MMMM d, yyyy}.");
        }
        if (SnapshotSections.Includes(selectedSections, SnapshotSections.Filings) && snapshot.RecentFilings.Count == 0)
            warnings.Add("SEC filing data is unavailable for this company and date.");
        if (snapshot.FailedSections.Count > 0)
            warnings.Add("Some sections of this investigation are unavailable.");
        if (SnapshotSections.Includes(selectedSections, SnapshotSections.News) && snapshot.RecentNews.Count == 0)
            warnings.Add($"No historical {NewsSources.DisplayName(selectedNewsSource)} news was found for this period. This does not mean nothing happened — our sources do not have full coverage for this company and date.");

        // Live quote is post-cutoff context for the reveal only. Best-effort:
        // never fails the historical snapshot.
        LiveQuoteDto? liveQuote = null;
        try
        {
            var quote = await _quotes.GetQuoteAsync(snapshot.CompanySymbol, ct);
            if (quote is not null)
                liveQuote = new LiveQuoteDto(quote.CurrentPrice, quote.Change, quote.PercentChange,
                    quote.High, quote.Low, quote.PreviousClose, quote.AsOfUtc, quote.Source);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Live quote enrichment failed for {Symbol}", snapshot.CompanySymbol);
        }

        var response = new SnapshotResponse(
            Company: MapCompany(snapshot.Company, snapshot.CompanySymbol),
            SnapshotDate: snapshot.SnapshotDate,
            CutoffUtc: cutoff,
            Price: new PriceQuoteDto(snapshot.Open, snapshot.High, snapshot.Low, snapshot.Price, snapshot.Volume, snapshot.SnapshotDate),
            RecentPrices: snapshot.RecentPrices.Select(p => new PricePointDto(p.Date, p.Open, p.High, p.Low, p.Close, p.Volume)).ToList(),
            Filings: snapshot.RecentFilings
                .Where(f => !f.IsMaterialDisclosure)
                .Select(f => new FilingDto(f.AccessionNumber, f.FormType, DateTime.SpecifyKind(f.FiledAt, DateTimeKind.Utc), DateTime.SpecifyKind(f.PeriodOfReport, DateTimeKind.Utc), f.Url, f.Summary ?? ""))
                .ToList(),
            CorporateDisclosures: snapshot.RecentFilings
                .Where(f => f.IsMaterialDisclosure)
                .Select(f => new DisclosureDto(f.AccessionNumber, f.FormType, DateTime.SpecifyKind(f.FiledAt, DateTimeKind.Utc), f.Url, f.Summary ?? $"{f.FormType} filing"))
                .ToList(),
            News: snapshot.RecentNews.Select(n => new NewsDto(n.Title, n.Source, DateTime.SpecifyKind(n.PublishedAt, DateTimeKind.Utc), n.Url)).ToList(),
            NewsSource: selectedNewsSource,
            Outcome: new OutcomeDto(
                snapshot.OutcomePrice,
                snapshot.OutcomePrices.Select(p => new PricePointDto(p.Date, p.Open, p.High, p.Low, p.Close, p.Volume)).ToList(),
                snapshot.OutcomeFilings.Select(f => new FilingDto(f.AccessionNumber, f.FormType, DateTime.SpecifyKind(f.FiledAt, DateTimeKind.Utc), DateTime.SpecifyKind(f.PeriodOfReport, DateTimeKind.Utc), f.Url, f.Summary ?? "")).ToList(),
                liveQuote),
            Warnings: warnings);

        return response;
    }

    // Live (delayed) quote endpoint. Server-side Finnhub call; the token never
    // reaches the browser. Cached 60s per symbol (Finnhub free tier: 60/min).
    [HttpGet("quote")]
    public async Task<ActionResult<LiveQuoteDto>> Quote([FromQuery] string? symbol, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new InvalidHistoricalDateException("Symbol is required.");

        var quote = await _quotes.GetQuoteAsync(symbol, ct);
        if (quote is null)
            throw new HistoricalDataNotFoundException($"Live quote is unavailable for {symbol.Trim().ToUpperInvariant()} at this time.");

        return Ok(new LiveQuoteDto(quote.CurrentPrice, quote.Change, quote.PercentChange,
            quote.High, quote.Low, quote.PreviousClose, quote.AsOfUtc, quote.Source));
    }

    [HttpPost("simulation")]
    public async Task<ActionResult<SimulationResponse>> RunSimulation([FromBody] SimulationRequest request, CancellationToken ct)
    {
        if (request is null) throw new InvalidHistoricalDateException("Request body required.");
        if (string.IsNullOrWhiteSpace(request.Symbol)) throw new InvalidHistoricalDateException("Symbol is required.");
        if (request.Amount <= 0) throw new InvalidHistoricalDateException("Amount must be greater than zero.");
        if (request.ExitDate.HasValue && request.ExitDate.Value < request.EntryDate)
            throw new InvalidHistoricalDateException("Exit date must be on or after the entry date.");

        var result = await _simulation.Run(request.Symbol, request.EntryDate, request.Amount, request.ExitDate, ct);
        return Ok(new SimulationResponse(
            result.EntryPrice,
            result.SharesPurchased,
            result.ExitPrice,
            result.FinalValue,
            result.ReturnPercentage,
            result.InvestmentAmount,
            result.EntryDate,
            result.ExitDate,
            SimulationDisclaimer));
    }

    [HttpGet("methodology")]
    public ActionResult<MethodologyDoc> Methodology()
    {
        return Ok(new MethodologyDoc(
            Title: "Methodology",
            Intro: "Stock Time Machine reconstructs what an investor could have known at a specific moment in market history. We do not predict; we remember with discipline.",
            Sections: new List<MethodologySection>
            {
                new("Temporal Boundary", "Each historical date maps to a strict cutoff at 23:59:59 US/Eastern on that calendar date. SEC filings are eligible by their SEC acceptance timestamp, news by publication time, and daily prices by trading day. Anything after the cutoff is excluded from the historical view and shown separately as outcome data."),
                new("Providers", "Historical OHLCV prices come from Alpha Vantage. SEC filings come from SEC EDGAR. Company identity is resolved from our directory, SEC EDGAR, then Finnhub. Current (delayed) quotes for the reveal come from Finnhub. API keys stay on the server and are never sent to the browser."),
                new("News Sources", "You choose the news source for each investigation: Alpha Vantage, GDELT, or MarketAux (entity-tagged finance news with per-article timestamps; free tier 100 requests/day, recent-years coverage — January 2020 returns empty while January 2025 returns results). GDELT is served by GDELT Cloud (entity-anchored stories with article evidence; corpus starts March 2026, so older windows return empty; each fetch takes the newest 100 rows of the trailing 7-day window, so unusually busy weeks can still truncate) when a server-side key is configured, otherwise the keyless GDELT Project archive. The selected source is shown on every snapshot and its results carry source attribution. Sources are never mixed and never silently substituted: if the selected source has no coverage, you see an honest empty state, not another provider's data in disguise."),
                new("Determinism", "Given the same company, date, news source, and persisted data, the snapshot engine returns the same result. Snapshots are composed from stored rows; the database is always checked before any external call."),
                new("Simulation", "The 'What if?' simulation uses raw, unadjusted prices. Stock splits and dividends are not factored into entry or exit prices. The disclaimer is always shown."),
                new("Key Moves — Last 100 Trading Days", "Significant movements are detected deterministically over the last 100 trading days before your date (minimum 30 required, else the lens reports insufficient history). Score = 0.5 × min(|z|/3, 1) + 0.3 × min(max(volumeRatio − 1, 0)/4, 1) + 0.2 × rangeBreak, where z is the daily-return z-score versus the trailing 20 days, volumeRatio is volume versus the trailing-20-day median, and rangeBreak is the fractional close beyond the trailing-20-day high/low (×20, so a 5% break scores full). Top 5 by score; ties break by later date, then larger move. A 0.05% daily volatility floor keeps perfectly flat windows from hiding genuine spikes. Flags: spike (z > 2), plunge (z < −2), high-volume (> 2.5× median), breakout/breakdown. Ranking statistics use double precision for ordering only — every displayed price is a real decimal close. Each move carries only evidence available by its own date cutoff; proximity in time is never presented as causation. News is DB-first, but a non-empty cache never shadows later coverage: when the newest cached row predates the latest move by over 7 days, one live refresh runs per investigation."),
                new("Decision Uncertainty Index", "A transparent 0–100 gauge of how thin or conflicting the knowable evidence is (higher = more uncertain). Score = 100 × (0.4 × evidence-sparsity + 0.3 × sentiment-dispersion + 0.3 × volatility-level), where sparsity compares evidence items against a full window, dispersion is the std of available per-entity sentiment scores scaled by 0.5, and volatility is annualized window volatility scaled by 50%. Fewer than 2 scored articles contributes 0.5 flagged as unknown — missing sentiment never reads as consensus. Every term is displayed alongside the score; there are no hidden inputs and no action thresholds."),
                new("Market Regimes", "Each analyzed day is labeled calm, normal, or tense from its trailing-20-day annualized volatility, tertiled within that investigation window only — never against absolute thresholds, so regimes are comparable across eras only relatively. Days with fewer than 10 prior closes read warming (insufficient trailing data). Tense stretches shade the moves timeline background. Regimes describe realized volatility; they predict nothing and trigger no action."),
                new("Sentiment Divergence", "For each key move, scored news sentiment is compared against the price direction: same sign reads agree, opposite signs disagree, mean within ±0.1 reads neutral, and fewer than two scored articles reads unknown (single scores are noise, never consensus). Scores come from providers that supply per-entity sentiment; rows without scores are ignored, not zero-filled. A disagreement is a contrarian lens for investigation, never a prediction and never evidence of causation."),
                new("Information Arrival Map", "For each key move, the arrival cascade lists when each information layer — regulatory filings, news, social discussion — first carried something knowable about that move, with lags measured against the earliest layer. Timestamps come from the evidence itself (filing dates, publication instants); GDELT Cloud items resolve to calendar days. A silent layer means no evidence was found there, never that nothing existed. Ordering in time is observation, not causation."),
                new("Narrative Topics", "The narratives endpoint clusters up to 60 cached articles (predominantly English) from the selected news source by keyword overlap: TF-IDF vectors (stopwords removed, no stemming) compared by cosine similarity, merged by greedy agglomerative clustering at a 0.25 similarity threshold. Each cluster is labeled by its three highest-weight terms and illustrated with its most distinctive article — labels name shared vocabulary, not meaning, and never imply events, causes, or importance. Non-English strays surface as their own threads rather than corrupting others. Clustering runs over cached rows only, so it costs zero API quota and is fully deterministic for a given database state; an empty cache yields an honest empty state. Coverage gaps in the underlying source propagate here unchanged."),
                new("AI Cluster Briefs", "Threads may carry an AI-generated brief: Gemini embeddings decide thread membership, and a Gemini Flash model summarizes grounded article text (Jina Reader bodies where fetchable, otherwise stored titles and descriptions) under a prompt that roleplays the investigation date, demands per-claim citations, and bans causation, prediction, and outside knowledge. Briefs appear for multi-article threads only (largest 8). They are NON-deterministic, may contain hindsight leaking from post-cutoff training data, and are labeled as AI-generated wherever shown. Embeddings still decide membership while shared terms name each thread, so labels stay explainable. Any AI failure degrades to the deterministic TF-IDF path, and the response states which method produced it. Scale guards: over-long articles are chunked and mean-pooled for embeddings (never silently truncated), and thread text past the model's input budget is briefed in batches then synthesized by a final reduce call with global citation numbering preserved. All AI calls share a 30,000-tokens-per-minute budget that waits instead of failing. The evidence copilot (filing summaries, article contrast, uncertainty explainers, English gists) reuses the same containment contract on demand, never automatically; the conclusion reviewer checks the user's own note against cited evidence and reports supported/unsupported/unclear per claim without authoring conclusions."),
                new("Limitations", "We do not claim complete historical news coverage: absence of coverage does not mean absence of events. We do not adjust for splits or dividends. We do not provide same-day tick data or intraday precision. One provider failing never destroys an investigation — affected sections are marked unavailable.")
            }));
    }

    private CompanySummaryDto MapCompany(Company? c, string symbol)
    {
        if (c is not null && !string.IsNullOrEmpty(c.Name))
            return new CompanySummaryDto(c.Symbol, c.Name, c.Cik ?? "", c.Exchange ?? "", c.Sector ?? "");

        if (_directory.TryGet(symbol, out var info) && info is not null)
            return new CompanySummaryDto(info.Symbol, info.Name, info.Cik, info.Exchange, info.Sector);

        return new CompanySummaryDto(symbol.ToUpperInvariant(), symbol.ToUpperInvariant(), "", "", "");
    }
}
