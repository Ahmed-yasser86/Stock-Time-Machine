using System.Text;

namespace StockTimeMachine;

// The single prompt behind every AI cluster brief. Designed for containment,
// not cleverness:
// - Cutoff roleplay: the model is told today's date is the investigation date
//   and must not use anything learned after it (mitigates, never eliminates,
//   hindsight leakage from post-cutoff training data).
// - Extractive discipline: only claims present in the supplied articles, each
//   cited [n]; disagreements and gaps are first-class output sections.
// - Banned moves, stated up front: causation ("caused the price move"),
//   prediction, outside context, and treating one article as consensus.
// Callers bound the input (article count × chars); this builder only shapes it.
public static class ClusterBriefPrompt
{
    public static string Build(
        string companySymbol,
        DateOnly asOfDate,
        IReadOnlyList<(string Title, string Body)> articles)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"You are a historical research assistant. Today is {asOfDate:yyyy-MM-dd}.");
        sb.AppendLine($"You know NOTHING that happened after this date. Never use outside knowledge,");
        sb.AppendLine($"never mention events after this date, and never infer what followed.");
        sb.AppendLine();
        sb.AppendLine($"Below are {articles.Count} contemporary articles about {companySymbol}, all published");
        sb.AppendLine($"on or before today, all covering one story thread. Summarize what THEY report.");
        sb.AppendLine();
        sb.AppendLine("Hard rules:");
        sb.AppendLine("- State only claims present in at least one article below; cite each claim like [1], [2].");
        sb.AppendLine("- NEVER state or imply causation with any stock price move.");
        sb.AppendLine("- NEVER predict, advise, or recommend anything.");
        sb.AppendLine("- One article alone is never consensus: say 'one article reports...' when unsourced elsewhere.");
        sb.AppendLine("- If the articles disagree or leave gaps, say so explicitly.");
        sb.AppendLine();
        sb.AppendLine("Respond with exactly these sections:");
        sb.AppendLine("SUMMARY: one paragraph, max 120 words, of what the coverage collectively reports.");
        sb.AppendLine("KEY POINTS: up to 5 bullets, each cited.");
        sb.AppendLine("DISAGREEMENTS AND GAPS: what is contested or missing; 'none visible' if uniform.");
        sb.AppendLine();
        for (int i = 0; i < articles.Count; i++)
        {
            sb.AppendLine($"[{i + 1}] {articles[i].Title}");
            if (!string.IsNullOrWhiteSpace(articles[i].Body))
                sb.AppendLine(articles[i].Body);
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
