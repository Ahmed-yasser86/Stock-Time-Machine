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
    // Mean measured per-article sentiment behind the direction verdict.
    // Null when fewer than 2 measured scores existed (additive, reason:
    // hype case vectors need a sentiment magnitude dim).
    public double? SentimentMean { get; set; }
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
    // Per-article publication dates, copied from the thread (additive,
    // reason: same as TopicCluster.ArticleDates — retrospective dating).
    // Empty on rows frozen before this field; readers must tolerate that.
    public Dictionary<string, DateOnly> ArticleDates { get; set; } = new();
    public double? RelevanceRate { get; set; }
    public string TopCategory { get; set; } = "";
    public string? BriefSummary { get; set; }
}

// One brief input article: dated when the date is known, span-labeled when
// only the thread's range is known — never silently assigned to either side
// of the peak (retrospective dating rule).
public class HypeBriefArticle
{
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public DateOnly? PublishedAt { get; set; }
    public string SpanLabel { get; set; } = "";
}

public class HypeCaseEvidence
{
    public int NewsCount { get; set; }
    public int FilingCount { get; set; }
    public int SocialCount { get; set; }
    public List<string> NewsTitles { get; set; } = new();
    public List<string> UnavailableLayers { get; set; } = new();
    // Exact stage text (additive, reason: hype brief narrates the full
    // investigation stages, not thread titles alone). Capped at projection
    // time so CaseJson stays small. Absent on rows frozen before this field
    // existed — readers must tolerate nulls.
    public List<HypeCaseNewsItem> News { get; set; } = new();
    public List<HypeCaseFiling> Filings { get; set; } = new();
    public List<HypeCaseSocialPost> Social { get; set; } = new();
    public List<HypeCaseArrival> Arrival { get; set; } = new();
}

public class HypeCaseNewsItem
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Source { get; set; } = "";
    public DateTime PublishedAt { get; set; }
    public string Url { get; set; } = "";
}

public class HypeCaseFiling
{
    // Accession links to FilingSummaryRecord (additive, reason: brief reuse
    // reads stored summaries by accession instead of re-fetching).
    public string AccessionNumber { get; set; } = "";
    public string FormType { get; set; } = "";
    public DateTime FiledAt { get; set; }
    public string Url { get; set; } = "";
}

public class HypeCaseSocialPost
{
    public string Title { get; set; } = "";
    public string Excerpt { get; set; } = "";
    public string Community { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string Url { get; set; } = "";
}

public class HypeCaseArrival
{
    public string Layer { get; set; } = "";
    public DateTime? FirstSeen { get; set; }
    public string State { get; set; } = "";
    public double? LagHours { get; set; }
    public string Detail { get; set; } = "";
}

public class HypeCaseReaction
{
    public DateOnly Date { get; set; }
    public decimal Close { get; set; }
}
