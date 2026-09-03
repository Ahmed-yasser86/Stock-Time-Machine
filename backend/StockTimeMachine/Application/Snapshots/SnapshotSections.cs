
namespace StockTimeMachine;

// Section keys for rescoping an investigation (?sections=prices,filings).
// A rescoped snapshot resolves ONLY the requested sections: unrequested ones
// return empty without touching the database or any provider. This powers
// per-section retry and lets clients skip slow sources (e.g. an unreachable
// news provider) instead of waiting out their timeouts.
public static class SnapshotSections
{
    public const string Prices = "prices";
    public const string Filings = "filings";
    public const string News = "news";
    public const string Outcome = "outcome";

    public static readonly IReadOnlyList<string> All = new[] { Prices, Filings, News, Outcome };

    // Null/empty/whitespace input means "all sections" (null return).
    // Unknown keys throw (mapped to 400 by the API error pipeline).
    public static IReadOnlySet<string>? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var selected = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);

        var unknown = selected.Except(All, StringComparer.Ordinal).ToArray();
        if (unknown.Length > 0)
            throw new InvalidHistoricalDateException(
                $"Unknown section(s): {string.Join(", ", unknown)}. Valid sections: {string.Join(", ", All)}.");

        return selected.Count == 0 ? null : selected;
    }

    public static bool Includes(IReadOnlySet<string>? sections, string section) =>
        sections is null || sections.Contains(section, StringComparer.Ordinal);
}
