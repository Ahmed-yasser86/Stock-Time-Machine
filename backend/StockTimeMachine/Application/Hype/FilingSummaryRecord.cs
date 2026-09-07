namespace StockTimeMachine;

// Persisted per-filing summary: structured signal fields + short prose,
// generated once (job completion or first brief), reused by every later
// brief and by the case-vector filing dims. Keyed by SEC accession number:
// filings are immutable per accession (amendments get new accessions), so
// absence of a row is the only re-generation trigger — no re-fetch timers.
// Mirrors HypeCase (queryable keys + JSON detail shape).
public class FilingSummaryRecord
{
    public string AccessionNumber { get; set; } = "";
    public string FormType { get; set; } = "";
    // Structured extraction JSON: event_type, primary_topic,
    // market_relevance, sentiment (8-K) or financial_direction,
    // key_risk_flags, market_relevance (10-Q/10-K).
    public string StructuredJson { get; set; } = "";
    // Short prose for brief reuse (2-3 sentence findings + disclosures).
    public string Findings { get; set; } = "";
    public string Disclosures { get; set; } = "";
    public string ConfidenceNote { get; set; } = "";
    // SHA-256 of the extracted source text: audit + change detection.
    public string ContentHash { get; set; } = "";
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}

// Structured field vocabularies (whitelists enforced at parse time).
public static class FilingEventTypes
{
    public const string ManagementChange = "management_change";
    public const string Acquisition = "acquisition";
    public const string EarningsWarning = "earnings_warning";
    public const string Regulatory = "regulatory";
    public const string Other = "other";

    public static readonly IReadOnlyList<string> All = new List<string>
    {
        ManagementChange, Acquisition, EarningsWarning, Regulatory, Other,
    }.AsReadOnly();
}

public static class FilingRelevance
{
    public const string High = "high";
    public const string Medium = "medium";
    public const string Low = "low";

    public static double ToScore(string? value) =>
        string.Equals(value, High, StringComparison.OrdinalIgnoreCase) ? 1.0 :
        string.Equals(value, Medium, StringComparison.OrdinalIgnoreCase) ? 0.5 : 0.0;
}

public static class FilingSentiment
{
    public const string Positive = "positive";
    public const string Negative = "negative";
    public const string Neutral = "neutral";

    public static double ToScore(string? value) =>
        string.Equals(value, Positive, StringComparison.OrdinalIgnoreCase) ? 1.0 :
        string.Equals(value, Negative, StringComparison.OrdinalIgnoreCase) ? -1.0 : 0.0;
}
