namespace StockTimeMachine;

// Persisted hype-cycle case: one key move (the "peak") plus its pre-peak
// narrative, frozen at investigation time. Mirrors InvestigationJob (same
// queryable-keys + JSON-detail shape): registry rows survive job pruning so
// signal mining has a stable case library. Money stays decimal; statistics
// stay double.
public class HypeCase
{
    // "{SYMBOL}:{peak yyyy-MM-dd}" — stable across reinvestigations.
    public string Id { get; set; } = "";
    public string CompanySymbol { get; set; } = "";
    public DateOnly PeakDate { get; set; }
    public DateOnly DecisionDate { get; set; }
    public string NewsSource { get; set; } = NewsSources.Gdelt;
    public double Score { get; set; }
    public decimal DailyReturnPct { get; set; }
    // ";"-separated MoveFlags (avoids a value conversion for the small set).
    public string FlagsCsv { get; set; } = "";
    public string SentimentDirection { get; set; } = SentimentDivergence.Unknown;
    // Overall completeness: HypeCompleteness Full|Partial|Missing.
    public string Completeness { get; set; } = HypeCompleteness.Missing;
    // Full HypeCaseDetail JSON (threads, regimes, evidence, reaction).
    public string CaseJson { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public static class HypeCompleteness
{
    public const string Full = "full";
    public const string Partial = "partial";
    public const string Missing = "missing";
}

// Full case detail (serialized into HypeCase.CaseJson). Pre-peak window is
// [PeakDate − PrePeakDays, PeakDate]; only window content is stored.
public class HypeCaseDetail
{
    public string CompanySymbol { get; set; } = "";
    public DateOnly PeakDate { get; set; }
    public double Score { get; set; }
    public decimal DailyReturnPct { get; set; }
    public List<string> Flags { get; set; } = new();
    public string SentimentDirection { get; set; } = SentimentDivergence.Unknown;
    public List<HypeCaseThread> PrePeakThreads { get; set; } = new();
    // Regime path inside the pre-peak window ("yyyy-MM-dd" → label).
    public Dictionary<string, string> RegimePath { get; set; } = new();
    public HypeCaseEvidence Evidence { get; set; } = new();
    public List<HypeCaseReaction> Reaction { get; set; } = new();
    public double? UncertaintyScore { get; set; }
    public string? UncertaintyConfidence { get; set; }
    public Dictionary<string, string> CompletenessByArea { get; set; } = new();
}

public class HypeCaseThread
{
    public List<string> LabelTerms { get; set; } = new();
    public string RepresentativeTitle { get; set; } = "";
    public DateTime? SpanStart { get; set; }
    public DateTime? SpanEnd { get; set; }
    public List<string> ArticleIds { get; set; } = new();
    public double? RelevanceRate { get; set; }
    public string TopCategory { get; set; } = "";
    public string? BriefSummary { get; set; }
}

public class HypeCaseEvidence
{
    public int NewsCount { get; set; }
    public int FilingCount { get; set; }
    public int SocialCount { get; set; }
    public List<string> NewsTitles { get; set; } = new();
    public List<string> UnavailableLayers { get; set; } = new();
}

public class HypeCaseReaction
{
    public DateOnly Date { get; set; }
    public decimal Close { get; set; }
}
