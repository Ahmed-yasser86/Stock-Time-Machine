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
        // Copy lives in MethodologyContent: endpoint, UI, and AI grounding
        // can never drift apart.
        return Ok(new MethodologyDoc(
            Title: MethodologyContent.Title,
            Intro: MethodologyContent.Intro,
            Sections: MethodologyContent.Sections
                .Select(s => new MethodologySection(s.Heading, s.Body))
                .ToList()));
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
