using Microsoft.AspNetCore.Mvc;
using StockTimeMachine;
using StockTimeMachine.Web.Models.Dto;

namespace StockTimeMachine.Web.Controllers;

// Evidence copilot: opt-in AI actions over already-retrieved evidence.
// Every action is explicit (never auto-run), grounded, cited, and labeled;
// empty evidence or disabled AI yields null briefs, never errors.
[Route("api/timemachine/copilot")]
[ApiController]
public class CopilotController : ControllerBase
{
    private readonly ICopilotService _copilot;

    public CopilotController(ICopilotService copilot)
    {
        _copilot = copilot;
    }

    public sealed record CopilotRequest(
        string? Symbol, string? Date, string? NewsSource,
        IReadOnlyList<string>? Ids, string? Note);

    private static (string Symbol, DateOnly AsOfDate, string NewsSource) Parse(CopilotRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Symbol))
            throw new InvalidHistoricalDateException("Symbol is required.");
        if (!DateOnly.TryParse(req.Date, out var parsedDate))
            throw new InvalidHistoricalDateException("Date must be a valid yyyy-MM-dd value.");
        return (req.Symbol, parsedDate, NewsSources.Normalize(req.NewsSource));
    }

    private static ClusterBriefDto? Map(ClusterBrief? brief) =>
        brief is null ? null : new ClusterBriefDto(brief.Summary, brief.KeyPoints, brief.Model);

    [HttpPost("filings-summary")]
    public async Task<ActionResult<CopilotBriefResponse>> FilingsSummary([FromBody] CopilotRequest req, CancellationToken ct)
    {
        var (symbol, asOf, _) = Parse(req);
        var brief = await _copilot.SummarizeFilings(symbol, asOf, ct);
        return Ok(new CopilotBriefResponse(symbol.ToUpperInvariant(), asOf, "filings-summary", Map(brief)));
    }

    [HttpPost("contrast")]
    public async Task<ActionResult<CopilotBriefResponse>> Contrast([FromBody] CopilotRequest req, CancellationToken ct)
    {
        var (symbol, asOf, source) = Parse(req);
        var ids = (req.Ids ?? Array.Empty<string>()).Take(5).ToList();
        if (ids.Count < 2)
            throw new InvalidHistoricalDateException("At least two article ids are required.");
        var brief = await _copilot.ContrastArticles(symbol, asOf, source, ids, ct);
        return Ok(new CopilotBriefResponse(symbol.ToUpperInvariant(), asOf, "contrast", Map(brief)));
    }

    [HttpPost("explain-uncertainty")]
    public async Task<ActionResult<CopilotBriefResponse>> ExplainUncertainty([FromBody] CopilotRequest req, CancellationToken ct)
    {
        var (symbol, asOf, source) = Parse(req);
        var brief = await _copilot.ExplainUncertainty(symbol, asOf, source, ct);
        return Ok(new CopilotBriefResponse(symbol.ToUpperInvariant(), asOf, "explain-uncertainty", Map(brief)));
    }

    [HttpPost("gist")]
    public async Task<ActionResult<CopilotBriefResponse>> Gist([FromBody] CopilotRequest req, CancellationToken ct)
    {
        var (symbol, asOf, source) = Parse(req);
        var ids = (req.Ids ?? Array.Empty<string>()).Take(5).ToList();
        if (ids.Count == 0)
            throw new InvalidHistoricalDateException("At least one article id is required.");
        var brief = await _copilot.GistThread(symbol, asOf, source, ids, ct);
        return Ok(new CopilotBriefResponse(symbol.ToUpperInvariant(), asOf, "gist", Map(brief)));
    }

    [HttpPost("suggest")]
    public async Task<ActionResult<CopilotBriefResponse>> Suggest([FromBody] SuggestRequest req, CancellationToken ct)
    {
        var (symbol, asOf, source) = Parse(new CopilotRequest(req.Symbol, req.Date, req.NewsSource, null, null));
        var gaps = (req.Gaps ?? Array.Empty<string>()).Where(g => !string.IsNullOrWhiteSpace(g)).Take(5).ToList();
        if (gaps.Count == 0)
            throw new InvalidHistoricalDateException("At least one gap pointer is required.");
        var brief = await _copilot.SuggestNextSteps(symbol, asOf, source, gaps, ct);
        return Ok(new CopilotBriefResponse(symbol.ToUpperInvariant(), asOf, "suggest", Map(brief)));
    }

    public sealed record SuggestRequest(string? Symbol, string? Date, string? NewsSource, IReadOnlyList<string>? Gaps);

    public sealed record ExplainRequest(string? Question, string? Facts);

    [HttpPost("explain")]
    public async Task<ActionResult<ExplainerResponse>> Explain([FromBody] ExplainRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Question) || req.Question.Length > 500)
            throw new InvalidHistoricalDateException("A question of up to 500 characters is required.");
        var answer = await _copilot.ExplainMethodology(req.Question, req.Facts, ct);
        if (answer is null)
            return Ok(new ExplainerResponse(req.Question, "The explainer is unavailable right now — the methodology sections above stand on their own.", Array.Empty<string>(), ""));
        return Ok(new ExplainerResponse(req.Question, answer.Answer, answer.CitedSections, answer.Model));
    }

    [HttpPost("review")]
    public async Task<ActionResult<ReviewResponse>> Review([FromBody] CopilotRequest req, CancellationToken ct)
    {
        var (symbol, asOf, source) = Parse(req);
        if (string.IsNullOrWhiteSpace(req.Note))
            throw new InvalidHistoricalDateException("A note is required.");
        var issues = await _copilot.ReviewNote(symbol, asOf, source, req.Note, ct);
        return Ok(new ReviewResponse(
            symbol.ToUpperInvariant(), asOf,
            issues.Select(i => new NoteIssueDto(i.Ref, i.Verdict, i.Detail)).ToList()));
    }
}
