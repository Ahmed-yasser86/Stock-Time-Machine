namespace StockTimeMachine;

// Signal-relevance filter for LLM brief inputs. The gate decides admission;
// this filter decides narration: only financially / market-relevant content
// reaches the model. Social noise (memes, fictional claims, unrelated
// tickers) and UNRELATED/REPUTATIONAL threads are excluded HERE, before any
// token is spent — never inside the prompt. Pure and deterministic.
public static class HypeBriefInputFilter
{
    // Thread categories eligible for narration. Everything business-material
    // stays; UNRELATED (noise that slipped admission) and REPUTATIONAL
    // (celebrity/image coverage, not market signal) never narrate.
    public static readonly IReadOnlySet<string> BriefMaterialCategories =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "FINANCIAL", "MARKET", "PRODUCT", "OPERATIONS", "SUPPLY_CHAIN",
            "MANAGEMENT", "STRATEGY", "LEGAL", "REGULATORY", "TECHNOLOGY",
            "CUSTOMER_DEMAND", "COMPETITIVE", "MACROECONOMIC", "GEOPOLITICAL",
            "LABOR", "OTHER_MATERIAL",
        };

    public static IReadOnlyList<HypeCaseThread> FilterThreads(
        IEnumerable<HypeCaseThread> threads) =>
        threads
            .Where(t => t is not null && BriefMaterialCategories.Contains(t.TopCategory ?? ""))
            .ToList();

    // Deterministic materiality verdict per item: only RELEVANT passes.
    // Company mention alone never suffices (see MaterialityRules). The
    // company NAME (not just the ticker) is required — real coverage writes
    // "Nvidia", never "NVDA".
    public static IReadOnlyList<HypeCaseNewsItem> FilterNews(
        string symbol, string? companyName, IEnumerable<HypeCaseNewsItem> news) =>
        (news ?? Enumerable.Empty<HypeCaseNewsItem>())
            .Where(n => n is not null && MaterialityRules.Judge(
                symbol, companyName, n.Title ?? "", n.Description ?? "").Decision
                == RelevanceDecisions.Relevant)
            .ToList();

    public static IReadOnlyList<HypeCaseSocialPost> FilterSocial(
        string symbol, string? companyName, IEnumerable<HypeCaseSocialPost> posts) =>
        (posts ?? Enumerable.Empty<HypeCaseSocialPost>())
            .Where(p => p is not null && MaterialityRules.Judge(
                symbol, companyName,
                p.Title ?? "", (p.Excerpt ?? "")).Decision
                == RelevanceDecisions.Relevant)
            .ToList();
}
