using Microsoft.Extensions.Logging;

namespace StockTimeMachine;

public class HypeBriefService : IHypeBriefService
{
    private const int MaxInputs = 5;
    private const int MaxBodyChars = 1500;

    private readonly IGeminiClient _gemini;
    private readonly ILogger<HypeBriefService> _logger;

    public HypeBriefService(IGeminiClient gemini, ILogger<HypeBriefService> logger)
    {
        _gemini = gemini;
        _logger = logger;
    }

    public async Task<ClusterBrief?> BriefSignalAsync(
        string symbol,
        DateOnly asOfDate,
        HypeSignalMatch match,
        HypeCaseDetail detail,
        CancellationToken ct = default)
    {
        if (match is null || detail is null)
            return null;
        if (!_gemini.IsEnabled)
            return null;
        try
        {
            // Grounding = the triggering threads (rep title + stored brief or
            // label terms), same input shape as narrative thread briefs.
            var wanted = new HashSet<string>(match.TriggerThreadIds, StringComparer.Ordinal);
            var threads = detail.PrePeakThreads
                .Where(t => t.ArticleIds.Any(id => wanted.Contains(id)))
                .ToList();
            if (threads.Count == 0)
                threads = detail.PrePeakThreads.Take(MaxInputs).ToList();
            var inputs = new List<(string Title, string Body)>();
            foreach (var t in threads.Take(MaxInputs))
            {
                var body = !string.IsNullOrWhiteSpace(t.BriefSummary)
                    ? t.BriefSummary!
                    : string.Join(" ", t.LabelTerms);
                if (body.Length > MaxBodyChars)
                    body = body.Substring(0, MaxBodyChars);
                inputs.Add((t.RepresentativeTitle, body));
            }
            if (inputs.Count == 0)
                return null;
            var prompt = ClusterBriefPrompt.Build(symbol.Trim().ToUpperInvariant(), asOfDate, inputs);
            return await _gemini.SummarizeClusterAsync(prompt, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hype signal brief failed for {Signal}; continuing without", match.SignalId);
            return null;
        }
    }
}
