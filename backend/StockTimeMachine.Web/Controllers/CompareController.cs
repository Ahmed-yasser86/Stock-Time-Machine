using Microsoft.AspNetCore.Mvc;
using StockTimeMachine;
using StockTimeMachine.Web.Models.Dto;

namespace StockTimeMachine.Web.Controllers;

// Cross-company surface. Read-only joins over per-company caches: no new
// providers, no pooled verdicts. The brief endpoint is opt-in AI (same
// containment contract as cluster briefs); everything else here is
// deterministic vocabulary overlap computed client-side from /moves +
// /narratives, so this controller stays thin by design.
[Route("api/timemachine/compare")]
[ApiController]
public class CompareController : ControllerBase
{
    private readonly INarrativeService _narratives;

    public CompareController(INarrativeService narratives)
    {
        _narratives = narratives;
    }

    // Shared-story brief across picks' cached coverage. Explicit opt-in only.
    [HttpGet("brief")]
    public async Task<ActionResult<CompareBriefResponse>> Brief(
        [FromQuery] string? symbols,
        [FromQuery] string? date,
        [FromQuery] string? newsSource,
        [FromQuery] string? terms,
        CancellationToken ct)
    {
        // Two-company scope: quota is not a blocker at this size, and every
        // shared view is designed around pairs.
        var picks = (symbols ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToUpperInvariant()).Distinct().Take(2).ToList();
        if (picks.Count < 2)
            throw new InvalidHistoricalDateException("At least two symbols are required.");
        if (!DateOnly.TryParse(date, out var parsedDate))
            throw new InvalidHistoricalDateException("Date must be a valid yyyy-MM-dd value.");
        var sharedTerms = (terms ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToList();
        if (sharedTerms.Count == 0)
            throw new InvalidHistoricalDateException("At least one shared term is required.");

        var selectedNewsSource = NewsSources.Normalize(newsSource);
        var brief = await _narratives.BriefSharedThread(picks, parsedDate, selectedNewsSource, sharedTerms, ct);

        return Ok(new CompareBriefResponse(
            Symbols: picks,
            AsOfDate: parsedDate,
            NewsSource: selectedNewsSource,
            Terms: sharedTerms,
            Brief: brief is null ? null : new ClusterBriefDto(brief.Summary, brief.KeyPoints, brief.Model)));
    }

    // Cross-pick thread pairs ranked by embedding cosine. Scores are shown so
    // users can judge each pair; titles link back to the owning lenses.
    [HttpGet("threads")]
    public async Task<ActionResult<CompareThreadsResponse>> Threads(
        [FromQuery] string? symbols,
        [FromQuery] string? date,
        [FromQuery] string? newsSource,
        CancellationToken ct)
    {
        var picks = (symbols ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToUpperInvariant()).Distinct().Take(2).ToList();
        if (picks.Count != 2)
            throw new InvalidHistoricalDateException("Exactly two symbols are required.");
        if (!DateOnly.TryParse(date, out var parsedDate))
            throw new InvalidHistoricalDateException("Date must be a valid yyyy-MM-dd value.");

        var selectedNewsSource = NewsSources.Normalize(newsSource);
        var pairs = await _narratives.CrossThreadSimilarity(picks, parsedDate, selectedNewsSource, ct);

        return Ok(new CompareThreadsResponse(
            Symbols: picks,
            AsOfDate: parsedDate,
            NewsSource: selectedNewsSource,
            Pairs: pairs.Select(p => new CrossThreadPairDto(
                p.ASymbol, p.ATitle, p.BSymbol, p.BTitle, p.Similarity)).ToList()));
    }
}
