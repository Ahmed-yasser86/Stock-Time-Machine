namespace StockTimeMachine;

// One significant price movement inside the 100-trading-day investigation window.
// Detection is deterministic statistics (see MoveDetectionService); all money
// stays decimal. Statistics (z-scores, ratios) are doubles used for ranking only.
public class KeyMove
{
    public DateOnly Date { get; set; }
    public decimal Close { get; set; }
    public decimal DailyReturnPct { get; set; }
    public double ZScore { get; set; }
    public double VolumeRatio { get; set; }
    public decimal FiveDayMomentumPct { get; set; }
    public double Score { get; set; }
    public List<string> Flags { get; set; } = new();
}

// Flag constants. Display copy must use temporal language ("occurred around",
// "published before") — never causal claims.
public static class MoveFlags
{
    public const string Spike = "spike";           // unusually large up day (z > 2)
    public const string Plunge = "plunge";         // unusually large down day (z < -2)
    public const string HighVolume = "high-volume"; // volume > 2.5x trailing median
    public const string Breakout = "breakout";     // close above trailing-20d high
    public const string Breakdown = "breakdown";   // close below trailing-20d low
}

// Market reaction: already-validated closes after the move (read-only market data).
public class MarketReaction
{
    public DateOnly Date { get; set; }
    public decimal Close { get; set; }
}

// Per-move evidence, every item already filtered to the move's own cutoff
// server-side. Empty lists are honest (provider down or no coverage), never padded.
public class MoveEvidence
{
    public List<SecFiling> Filings { get; set; } = new();
    public List<NewsArticle> News { get; set; } = new();
    public List<SocialSignal> Social { get; set; } = new();
    public List<MarketReaction> Reaction { get; set; } = new();
    public List<string> UnavailableLayers { get; set; } = new();
}

// Window-level investor context for the 100 trading days before the decision.
public class WindowSummary
{
    public int TradingDays { get; set; }
    public decimal CumulativeReturnPct { get; set; }
    public double Volatility { get; set; }
    public decimal MaxDrawdownPct { get; set; }
    public DateOnly? BestDay { get; set; }
    public decimal BestDayReturnPct { get; set; }
    public DateOnly? WorstDay { get; set; }
    public decimal WorstDayReturnPct { get; set; }
    public bool SufficientHistory { get; set; }
}

// Aggregate root returned for one investigation window.
public class MovesWindow
{
    public string CompanySymbol { get; set; } = "";
    public DateOnly DecisionDate { get; set; }
    public string NewsSource { get; set; } = NewsSources.Gdelt;
    public WindowSummary Summary { get; set; } = new();
    public List<KeyMove> KeyMoves { get; set; } = new();
    public Dictionary<string, MoveEvidence> EvidenceByDate { get; set; } = new();
    // The analyzed trading-day slice (ascending) backing the timeline view.
    public List<PricePoint> WindowPrices { get; set; } = new();
}
