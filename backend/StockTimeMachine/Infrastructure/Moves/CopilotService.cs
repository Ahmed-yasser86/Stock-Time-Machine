using System.Text;
using Microsoft.Extensions.Logging;

namespace StockTimeMachine;

// Evidence copilot: every action grounds in already-retrieved rows and returns
// through the same containment prompt family as cluster briefs. When AI is off
// or evidence is empty, every method returns null (or empty) — the UI then
// shows the evidence standing on its own, never an error.
public class CopilotService : ICopilotService
{
    private const int MaxItems = 5;
    private const int MaxBodyChars = 1500;

    private readonly IHistoricalDataRepository _dataRepo;
    private readonly IMoveDetectionService _moves;
    private readonly IGeminiClient _gemini;
    private readonly IArticleContentClient _bodies;
    private readonly ILogger<CopilotService> _logger;

    public CopilotService(
        IHistoricalDataRepository dataRepo,
        IMoveDetectionService moves,
        IGeminiClient gemini,
        IArticleContentClient bodies,
        ILogger<CopilotService> logger)
    {
        _dataRepo = dataRepo;
        _moves = moves;
        _gemini = gemini;
        _bodies = bodies;
        _logger = logger;
    }

    public async Task<ClusterBrief?> SummarizeFilings(string symbol, DateOnly asOfDate, CancellationToken ct = default)
    {
        try
        {
            var normalized = Require(symbol, asOfDate);
            if (!_gemini.IsEnabled)
                return null;
            var filings = (await _dataRepo.GetFilingsAsOf(normalized, asOfDate, ct)).Take(MaxItems).ToList();
            if (filings.Count == 0)
                return null;
            var sb = Header(normalized, asOfDate);
            sb.AppendLine($"Below are {filings.Count} SEC filings available on or before today. Summarize what THEY disclose.");
            Containment(sb);
            sb.AppendLine("Respond with exactly these sections:");
            sb.AppendLine("SUMMARY: one paragraph, max 120 words.");
            sb.AppendLine("KEY POINTS: up to 5 bullets, each cited [n].");
            sb.AppendLine("DISAGREEMENTS AND GAPS: contested or missing; 'none visible' if uniform.");
            sb.AppendLine();
            for (int i = 0; i < filings.Count; i++)
                sb.AppendLine($"[{i + 1}] {filings[i].FormType} filed {filings[i].FiledAt:yyyy-MM-dd}: {filings[i].Summary ?? filings[i].FormType}\n");
            return await _gemini.SummarizeClusterAsync(sb.ToString(), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Copilot filings summary failed");
            return null;
        }
    }

    public async Task<ClusterBrief?> ContrastArticles(string symbol, DateOnly asOfDate, string? newsSource, IReadOnlyList<string> articleIds, CancellationToken ct = default)
    {
        try
        {
            var normalized = Require(symbol, asOfDate);
            if (!_gemini.IsEnabled)
                return null;
            var selected = NewsSources.Normalize(newsSource);
            var cached = await _dataRepo.GetNewsAsOf(normalized, asOfDate, ct);
            var docs = cached
                .Where(n => IsFromSource(n, selected) && articleIds.Contains(n.Id))
                .Take(MaxItems).ToList();
            if (docs.Count < 2)
                return null;
            var inputs = await GroundAsync(docs, ct);
            var sb = Header(normalized, asOfDate);
            sb.AppendLine($"Below are {inputs.Count} contemporary articles. State precisely where they AGREE and where they DISAGREE.");
            Containment(sb);
            sb.AppendLine("Respond with exactly these sections:");
            sb.AppendLine("SUMMARY: one paragraph, max 120 words, of the common ground.");
            sb.AppendLine("KEY POINTS: up to 5 bullets, each cited [n].");
            sb.AppendLine("DISAGREEMENTS AND GAPS: every point of disagreement first, then gaps; 'none visible' if uniform.");
            sb.AppendLine();
            Numbered(sb, inputs);
            return await _gemini.SummarizeClusterAsync(sb.ToString(), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Copilot contrast failed");
            return null;
        }
    }

    public async Task<ClusterBrief?> ExplainUncertainty(string symbol, DateOnly asOfDate, string? newsSource, CancellationToken ct = default)
    {
        try
        {
            var normalized = Require(symbol, asOfDate);
            if (!_gemini.IsEnabled)
                return null;
            var window = await _moves.GetMoves(normalized, asOfDate, newsSource, ct);
            var u = window.Uncertainty;
            if (u is null)
                return null;
            var sb = Header(normalized, asOfDate);
            sb.AppendLine($"The decision-uncertainty score for this window is {u.Score:F1} of 100, from these measured components. Explain each component in plain words for a non-technical investor.");
            sb.AppendLine("Hard rules:");
            sb.AppendLine("- Use ONLY the numbers below. Never invent numbers, thresholds, or advice.");
            sb.AppendLine("- NEVER predict, advise, or recommend anything.");
            sb.AppendLine("- Higher score means thinner or more conflicting evidence — say what is thin, concretely.");
            sb.AppendLine();
            sb.AppendLine("Respond with exactly these sections:");
            sb.AppendLine("SUMMARY: one paragraph, max 100 words, saying what drives the score.");
            sb.AppendLine("KEY POINTS: one bullet per component below, quoting its numbers.");
            sb.AppendLine("DISAGREEMENTS AND GAPS: which evidence is missing or conflicting; 'none visible' if complete.");
            sb.AppendLine();
            int i = 1;
            foreach (var c in u.Components)
                sb.AppendLine($"[{i++}] {c.Name} (weight {(c.Weight * 100):F0}%, value {c.Value:F3}): {c.Detail}");
            return await _gemini.SummarizeClusterAsync(sb.ToString(), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Copilot uncertainty explainer failed");
            return null;
        }
    }

    public async Task<ClusterBrief?> GistThread(string symbol, DateOnly asOfDate, string? newsSource, IReadOnlyList<string> articleIds, CancellationToken ct = default)
    {
        try
        {
            var normalized = Require(symbol, asOfDate);
            if (!_gemini.IsEnabled)
                return null;
            var selected = NewsSources.Normalize(newsSource);
            var cached = await _dataRepo.GetNewsAsOf(normalized, asOfDate, ct);
            var docs = cached
                .Where(n => IsFromSource(n, selected) && articleIds.Contains(n.Id))
                .Take(MaxItems).ToList();
            if (docs.Count == 0)
                return null;
            var inputs = await GroundAsync(docs, ct);
            var sb = Header(normalized, asOfDate);
            sb.AppendLine($"Below are {inputs.Count} non-English articles. Render an English gist that preserves every checkable fact.");
            Containment(sb);
            sb.AppendLine("Also state the original language(s) first, then:");
            sb.AppendLine("SUMMARY: one paragraph, max 120 words.");
            sb.AppendLine("KEY POINTS: up to 5 bullets, each cited [n].");
            sb.AppendLine("DISAGREEMENTS AND GAPS: 'none visible' if uniform.");
            sb.AppendLine();
            Numbered(sb, inputs);
            var brief = await _gemini.SummarizeClusterAsync(sb.ToString(), ct);
            if (brief is not null)
                brief.Model += "+gist";
            return brief;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Copilot gist failed");
            return null;
        }
    }

    public async Task<IReadOnlyList<NoteIssue>> ReviewNote(string symbol, DateOnly asOfDate, string? newsSource, string note, CancellationToken ct = default)
    {
        var empty = Array.Empty<NoteIssue>();
        try
        {
            var normalized = Require(symbol, asOfDate);
            if (!_gemini.IsEnabled || string.IsNullOrWhiteSpace(note))
                return empty;
            var selected = NewsSources.Normalize(newsSource);
            var window = await _moves.GetMoves(normalized, asOfDate, selected, ct);
            var cached = await _dataRepo.GetNewsAsOf(normalized, asOfDate, ct);
            var fromSource = cached.Where(n => IsFromSource(n, selected)).Take(20).ToList();
            var sb = Header(normalized, asOfDate);
            sb.AppendLine("A user wrote the conclusion note below, citing evidence as [move YYYY-MM-DD] and [thread terms]. Check EVERY cited claim against the evidence ledger. You REVIEW — you never rewrite conclusions, never add new claims, never advise.");
            sb.AppendLine("Verdicts: supported (evidence backs it), unsupported (evidence contradicts or no cited evidence exists), unclear (cannot tell from this ledger).");
            sb.AppendLine();
            sb.AppendLine("EVIDENCE LEDGER — key moves:");
            foreach (var m in window.KeyMoves)
                sb.AppendLine($"- [move {m.Date:yyyy-MM-dd}] return {m.DailyReturnPct:F2}%, flags [{string.Join(",", m.Flags)}], sentiment {m.SentimentDirection}");
            sb.AppendLine("EVIDENCE LEDGER — cached article titles:");
            int i = 1;
            foreach (var n in fromSource)
                sb.AppendLine($"- ({i++}) {n.Title}");
            sb.AppendLine();
            sb.AppendLine("USER NOTE:");
            sb.AppendLine(note.Length > 4000 ? note.Substring(0, 4000) : note);
            return await _gemini.ReviewNoteAsync(sb.ToString(), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Copilot note review failed");
            return empty;
        }
    }

    private static string Require(string symbol, DateOnly asOfDate)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new InvalidHistoricalDateException("Symbol is required.");
        HistoricalDate.Create(asOfDate);
        return symbol.Trim().ToUpperInvariant();
    }

    private static StringBuilder Header(string symbol, DateOnly asOfDate)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"You are a historical research assistant. Today is {asOfDate:yyyy-MM-dd}.");
        sb.AppendLine($"You know NOTHING that happened after this date. Never use outside knowledge,");
        sb.AppendLine($"never mention events after this date, and never infer what followed.");
        sb.AppendLine();
        return sb;
    }

    private static void Containment(StringBuilder sb)
    {
        sb.AppendLine("Hard rules:");
        sb.AppendLine("- State only claims present in at least one item below; cite each claim like [1], [2].");
        sb.AppendLine("- NEVER state or imply causation with any stock price move.");
        sb.AppendLine("- NEVER predict, advise, or recommend anything.");
        sb.AppendLine("- One item alone is never consensus: say 'one item reports...' when unsourced elsewhere.");
        sb.AppendLine("- If items disagree or leave gaps, say so explicitly.");
        sb.AppendLine();
    }

    private static void Numbered(StringBuilder sb, List<(string Title, string Body)> inputs)
    {
        for (int i = 0; i < inputs.Count; i++)
        {
            sb.AppendLine($"[{i + 1}] {inputs[i].Title}");
            if (!string.IsNullOrWhiteSpace(inputs[i].Body))
                sb.AppendLine(inputs[i].Body);
            sb.AppendLine();
        }
    }

    private async Task<List<(string Title, string Body)>> GroundAsync(List<NewsArticle> docs, CancellationToken ct)
    {
        var inputs = new List<(string Title, string Body)>();
        int fetched = 0;
        foreach (var d in docs)
        {
            string bodyText = d.Description ?? "";
            if (_bodies.IsEnabled && fetched < 3 && !string.IsNullOrWhiteSpace(d.Url))
            {
                var fb = await _bodies.FetchBodyAsync(d.Url, ct);
                if (fb is not null)
                {
                    bodyText = fb.Markdown;
                    fetched++;
                }
            }
            if (bodyText.Length > MaxBodyChars)
                bodyText = bodyText.Substring(0, MaxBodyChars);
            inputs.Add((d.Title, bodyText));
        }
        return inputs;
    }

    private static bool IsFromSource(NewsArticle article, string newsSource)
    {
        var source = article.Source ?? "";
        if (newsSource == NewsSources.AlphaVantage)
            return source.Contains("Alpha Vantage", StringComparison.OrdinalIgnoreCase);
        if (newsSource == NewsSources.MarketAux)
            return source.Contains("MarketAux", StringComparison.OrdinalIgnoreCase);
        return source.Contains("GDELT", StringComparison.OrdinalIgnoreCase);
    }
}
