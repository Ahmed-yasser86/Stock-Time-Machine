namespace StockTimeMachine;

// One layer's first appearance for a movement: when this information layer
// first carried something knowable about the move. Lags are measured against
// the earliest observed layer. A silent layer is rendered as unknown — never
// as zero, never as evidence of absence.
public class ArrivalEntry
{
    public string Layer { get; set; } = "";
    public DateTime? FirstSeen { get; set; }
    public string State { get; set; } = ArrivalStates.Silent;
    public double? LagHours { get; set; }
    public string Detail { get; set; } = "";
}

public static class ArrivalLayers
{
    public const string Regulatory = "regulatory";
    public const string News = "news";
    public const string Social = "social";
    public const string Market = "market";
}

public static class ArrivalStates
{
    public const string Observed = "observed";
    public const string Silent = "silent";
}

// Pure, deterministic first-appearance computation over already-validated
// per-move evidence. No I/O, no clock, no randomness.
public static class ArrivalMap
{
    public static List<ArrivalEntry> Build(DateOnly moveDate, MoveEvidence evidence)
    {
        DateTime? firstFiling = evidence.Filings.Count > 0
            ? evidence.Filings.Min(f => f.FiledAt)
            : null;
        DateTime? firstNews = evidence.News.Count > 0
            ? evidence.News.Min(n => n.PublishedAt)
            : null;
        DateTime? firstSocial = evidence.Social.Count > 0
            ? evidence.Social.Min(s => s.CreatedAt)
            : null;
        var moveInstant = moveDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var observed = new List<(string Layer, DateTime Seen, string Detail)>();
        if (firstFiling.HasValue)
            observed.Add((ArrivalLayers.Regulatory, firstFiling.Value,
                $"{evidence.Filings.Count} filing(s) available"));
        if (firstNews.HasValue)
            observed.Add((ArrivalLayers.News, firstNews.Value,
                $"{evidence.News.Count} article(s) published"));
        if (firstSocial.HasValue)
            observed.Add((ArrivalLayers.Social, firstSocial.Value,
                $"{evidence.Social.Count} post(s) created"));
        observed.Add((ArrivalLayers.Market, moveInstant, "price movement recorded"));

        var earliest = observed.Min(o => o.Seen);
        var entries = observed
            .OrderBy(o => o.Seen)
            .Select(o => new ArrivalEntry
            {
                Layer = o.Layer,
                FirstSeen = o.Seen,
                State = ArrivalStates.Observed,
                LagHours = Math.Round((o.Seen - earliest).TotalHours, 1),
                Detail = o.Detail,
            })
            .ToList();

        foreach (var layer in new[] { ArrivalLayers.Regulatory, ArrivalLayers.News, ArrivalLayers.Social })
        {
            if (entries.Any(e => e.Layer == layer))
                continue;
            entries.Add(new ArrivalEntry
            {
                Layer = layer,
                FirstSeen = null,
                State = ArrivalStates.Silent,
                LagHours = null,
                Detail = "no evidence in this layer before the movement",
            });
        }

        return entries;
    }
}
