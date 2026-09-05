using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
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
    private readonly INarrativeService _narratives;
    private readonly ICompanyDirectory _directory;
    private readonly INewsProviderFactory _newsFactory;
    private readonly IInvestigationJobStore _jobs;
    private readonly IInvestigationJobRunner _runner;
    private readonly ILogger<MovesController> _logger;

    public MovesController(
        IMoveDetectionService moves,
        INarrativeService narratives,
        ICompanyDirectory directory,
        INewsProviderFactory newsFactory,
        IInvestigationJobStore jobs,
        IInvestigationJobRunner runner,
        ILogger<MovesController> logger)
    {
        _moves = moves;
        _narratives = narratives;
        _directory = directory;
        _newsFactory = newsFactory;
        _jobs = jobs;
        _runner = runner;
        _logger = logger;
    }

    public sealed record CreateJobRequest(string? Symbol, string? Date, string? NewsSource);
    public sealed record CreateJobResponse(string JobId);

    // Persisted background run: the timer starts now, the pipeline runs
    // detached from any connection, and the client follows via stream/{id}.
    [HttpPost("moves/jobs")]
    public async Task<ActionResult<CreateJobResponse>> CreateJob([FromBody] CreateJobRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Symbol))
            throw new InvalidHistoricalDateException("Symbol is required.");
        if (!DateOnly.TryParse(req.Date, out var parsedDate))
            throw new InvalidHistoricalDateException("Date must be a valid yyyy-MM-dd value.");
        HistoricalDate.Create(parsedDate);

        var id = await _runner.StartAsync(req.Symbol, parsedDate, req.NewsSource ?? _newsFactory.DefaultSource, ct);
        return Ok(new CreateJobResponse(id));
    }

    // Stream a persisted run: full history first (reconnect-safe), then live
    // tail by polling, heartbeat comments every 20s, finals on completion.
    // Disconnecting changes nothing — the job runs on. Unknown ids get an
    // error event so the client starts a fresh job instead.
    [HttpGet("moves/stream/{jobId}")]
    public async Task StreamJob([FromRoute] string jobId, CancellationToken ct)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";

        async Task WriteEvent(string name, object payload)
        {
            var json = JsonSerializer.Serialize(payload, MovesStreamJson);
            await Response.WriteAsync($"event: {name}\ndata: {json}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }

        async Task Heartbeat()
        {
            await Response.WriteAsync(": heartbeat\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }

        var job = await _jobs.GetAsync(jobId, ct);
        if (job is null)
        {
            await WriteEvent("error", new { detail = "Unknown investigation. Start a new one." });
            return;
        }
        if (job.Status == JobStatuses.Running && DateTime.UtcNow - job.CreatedAtUtc > _runner.Timeout)
        {
            await _jobs.ReapStaleAsync(_runner.Timeout, ct);
            job = await _jobs.GetAsync(jobId, ct);
        }
        if (job is null)
        {
            await WriteEvent("error", new { detail = "Unknown investigation. Start a new one." });
            return;
        }

        int sent = 0;
        var lastBeat = DateTime.UtcNow;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var stage in job.Stages.Skip(sent))
            {
                await WriteEvent("stage", new
                {
                    stage = stage.Stage,
                    state = stage.State,
                    detail = stage.Detail,
                    count = stage.Count
                });
                sent++;
            }
            if (JobStatuses.IsTerminal(job.Status))
            {
                if (job.Status == JobStatuses.Complete && job.MovesJson is not null && job.NarrativesJson is not null)
                {
                    var window = JsonSerializer.Deserialize<MovesWindow>(job.MovesJson, MovesStreamJson);
                    var topics = JsonSerializer.Deserialize<NarrativeTopicsResult>(job.NarrativesJson, MovesStreamJson);
                    if (window is not null)
                        await WriteEvent("moves", MapMoves(window));
                    if (topics is not null)
                        await WriteEvent("narratives", MapNarratives(topics));
                }
                else
                {
                    var detail = job.Status == JobStatuses.Timeout
                        ? "The investigation exceeded its one-hour limit and was stopped."
                        : "Something went wrong. Please try again.";
                    await WriteEvent("error", new { detail });
                }
                return;
            }
            if (DateTime.UtcNow - lastBeat > TimeSpan.FromSeconds(20))
            {
                await Heartbeat();
                lastBeat = DateTime.UtcNow;
            }
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
            catch (OperationCanceledException)
            {
                // Client went away: the persisted job keeps running. Nothing
                // to cancel, nothing to clean — just stop talking.
                return;
            }
            job = await _jobs.GetAsync(jobId, ct);
            if (job is null)
            {
                await WriteEvent("error", new { detail = "Unknown investigation. Start a new one." });
                return;
            }
            if (job.Status == JobStatuses.Running && DateTime.UtcNow - job.CreatedAtUtc > _runner.Timeout)
            {
                await _jobs.ReapStaleAsync(_runner.Timeout, ct);
                job = await _jobs.GetAsync(jobId, ct);
                if (job is null)
                {
                    await WriteEvent("error", new { detail = "Unknown investigation. Start a new one." });
                    return;
                }
            }
        }
    }

    // Window-level narrative threads from cached news (zero quota cost).
    // Keyword-overlap clustering (TF-IDF + cosine); EN-only; thresholded.
    [HttpGet("narratives")]
    public async Task<ActionResult<NarrativesResponse>> Narratives(
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
        var result = await _narratives.GetTopics(symbol, parsedDate, selectedNewsSource, ct);

        return Ok(MapNarratives(result));
    }

    private NarrativesResponse MapNarratives(NarrativeTopicsResult result) =>
        new NarrativesResponse(
            Company: MapCompany(result.CompanySymbol),
            AsOfDate: result.AsOfDate,
            NewsSource: result.NewsSource,
            ArticlesConsidered: result.ArticlesConsidered,
            ArticlesClustered: result.ArticlesClustered,
            ClusteringMethod: result.ClusteringMethod,
            Topics: result.Topics.Select(t => new TopicClusterDto(
                t.LabelTerms, t.ArticleIds, t.RepresentativeTitle,
                t.SpanStart, t.SpanEnd,
                t.Brief is null ? null : new ClusterBriefDto(
                    t.Brief.Summary, t.Brief.KeyPoints, t.Brief.Model))).ToList());

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

        return Ok(MapMoves(window));
    }

    // Live investigation stream: stage events (detecting → evidence per move
    // → embedding counts → threads → per-thread briefs) then the full `moves`
    // and `narratives` payloads. Same data as the two GETs, narrated while it
    // computes. Validation errors are normal 400s; mid-stream failures arrive
    // as an `error` event.
    [HttpGet("moves/stream")]
    public async Task MovesStream(
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

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";

        async Task WriteEvent(string name, object payload)
        {
            var json = JsonSerializer.Serialize(payload, MovesStreamJson);
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
            var window = await _moves.GetMoves(symbol, parsedDate, selectedNewsSource, ct, progress);
            await WriteEvent("moves", MapMoves(window));
            var topics = await _narratives.GetTopics(symbol, parsedDate, selectedNewsSource, ct, progress);
            await WriteEvent("narratives", MapNarratives(topics));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Moves stream failed for {Symbol} on {Date}", symbol, parsedDate);
            await WriteEvent("error", new { detail = "Something went wrong. Please try again." });
        }
    }

    private static readonly JsonSerializerOptions MovesStreamJson = new(JsonSerializerDefaults.Web);

    private MovesResponse MapMoves(MovesWindow window) =>
        new MovesResponse(
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
                m.FiveDayMomentumPct, m.Score, m.Flags, m.SentimentDirection)).ToList(),
            WindowPrices: window.WindowPrices.Select(p => new PricePointDto(p.Date, p.Open, p.High, p.Low, p.Close, p.Volume)).ToList(),
            Uncertainty: new UncertaintyIndexDto(
                window.Uncertainty.Score,
                window.Uncertainty.Components.Select(c => new UncertaintyComponentDto(
                    c.Name, c.Weight, c.Value, c.Detail)).ToList()),
            Regimes: window.Regimes,
            EvidenceByDate: window.EvidenceByDate.ToDictionary(
                kvp => kvp.Key,
                kvp => new MoveEvidenceDto(
                    kvp.Value.Filings.Select(f => new MoveFilingDto(
                        f.AccessionNumber, f.FormType,
                        DateTime.SpecifyKind(f.FiledAt, DateTimeKind.Utc), f.Url)).ToList(),
                    kvp.Value.News.Select(n => new MoveNewsDto(
                        n.Id, n.Title, n.Source,
                        DateTime.SpecifyKind(n.PublishedAt, DateTimeKind.Utc), n.Url,
                        n.SentimentScore)).ToList(),
                    kvp.Value.Social.Select(s => new SocialSignalDto(
                        s.Provider, s.Community, s.Title, s.Excerpt, s.Url,
                        DateTime.SpecifyKind(s.CreatedAt, DateTimeKind.Utc),
                        s.Score, s.CommentCount, s.Flair)).ToList(),
                    kvp.Value.Reaction.Select(r => new MarketReactionDto(r.Date, r.Close)).ToList(),
                    kvp.Value.UnavailableLayers,
                    kvp.Value.Arrival.Select(a => new ArrivalEntryDto(
                        a.Layer, a.FirstSeen, a.State, a.LagHours, a.Detail)).ToList())));

    private CompanySummaryDto MapCompany(string symbol)
    {
        if (_directory.TryGet(symbol, out var info) && info is not null)
            return new CompanySummaryDto(info.Symbol, info.Name, info.Cik, info.Exchange, info.Sector);

        return new CompanySummaryDto(symbol.ToUpperInvariant(), symbol.ToUpperInvariant(), "", "", "");
    }
}
