using System.Text;

namespace StockTimeMachine;

// The single prompt behind every relevance verdict. Containment-first:
// cutoff roleplay, extractive discipline (headline + context only), explicit
// bans (no temporal override, no sentiment, no scoring, no causation), and
// the conservative principle (material → relevant; weak/incidental → not).
public static class RelevancePrompt
{
    public static readonly IReadOnlySet<string> Categories = new HashSet<string>(StringComparer.Ordinal)
    {
        "FINANCIAL", "MARKET", "PRODUCT", "OPERATIONS", "SUPPLY_CHAIN",
        "MANAGEMENT", "STRATEGY", "LEGAL", "REGULATORY", "TECHNOLOGY",
        "CUSTOMER_DEMAND", "COMPETITIVE", "MACROECONOMIC", "GEOPOLITICAL",
        "REPUTATIONAL", "LABOR", "OTHER_MATERIAL", "UNRELATED",
    };

    public static string Build(
        string company, string ticker, DateOnly asOfDate,
        string? sector, IReadOnlyList<(string Id, string Headline)> headlines)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"You are a historical research assistant. Today is {asOfDate:yyyy-MM-dd}.");
        sb.AppendLine($"You know NOTHING that happened after this date. Use only the headlines below plus the investigation context.");
        sb.AppendLine();
        sb.AppendLine($"Target Company: {company}");
        sb.AppendLine($"Target Ticker: {ticker}");
        sb.AppendLine($"Investigation Date: {asOfDate:yyyy-MM-dd}");
        if (!string.IsNullOrWhiteSpace(sector))
            sb.AppendLine($"Sector / Industry: {sector}");
        sb.AppendLine();
        sb.AppendLine("For EACH headline, decide: does it contain information materially relevant to understanding");
        sb.AppendLine("this company's situation, events, risks, opportunities, market conditions, or decision environment");
        sb.AppendLine("at the investigation date? Perform SEMANTIC classification, not keyword matching:");
        sb.AppendLine("- A company mention alone (name, ticker, executive, incidental reference) does NOT make it relevant.");
        sb.AppendLine("- Absence of the company name does NOT make it irrelevant: material supplier, customer,");
        sb.AppendLine("  regulator, competitor, macroeconomic, or geopolitical connections count — when material and defensible.");
        sb.AppendLine("- Do NOT invent hypothetical connections to force relevance. Weak/incidental → NOT_RELEVANT.");
        sb.AppendLine("- Uncertain but potentially material → RELEVANT with lower confidence.");
        sb.AppendLine();
        sb.AppendLine("Hard bans: do NOT judge temporal admissibility (already validated). Do NOT score sentiment.");
        sb.AppendLine("Do NOT compute uncertainty, risk, or recommendations. Never predict, advise, or recommend anything.");
        sb.AppendLine("Do NOT state or imply causation.");
        sb.AppendLine("Classify each headline INDEPENDENTLY; one headline must not change another's verdict.");
        sb.AppendLine();
        sb.AppendLine("Respond with JSON only (no markdown): an object with a results array, one entry");
        sb.AppendLine("per headline in input order, each {id (exact input id), relevant (boolean),");
        sb.AppendLine("category (one of " + string.Join(", ", Categories) + "; UNRELATED when not relevant),");
        sb.AppendLine("confidence (0.0-1.0 for the classification only),");
        sb.AppendLine("reason (one concise factual sentence; never 'mentioned, therefore relevant')}.");
        sb.AppendLine();
        for (int i = 0; i < headlines.Count; i++)
            sb.AppendLine($"[{headlines[i].Id}] {headlines[i].Headline}");
        return sb.ToString();
    }
}
