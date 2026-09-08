namespace StockTimeMachine;

// Movement-level regulatory evidence window (methodology reg-v1).
// A movement may only claim regulatory evidence from its own candidate
// window: [moveDate − lookbackDays, moveDate]. Company history outside it
// (e.g. a 2015 filing for a 2026 move) is never movement-level evidence,
// no matter how the rows are stored. Pure and deterministic; the lookback
// is a parameter (default 30) so sensitivity runs (7/14/60) need no rewrite.
// This is an EXTRA layer over semantic analysis, not a replacement: the
// relevance gate still decides what is material, this decides what is
// temporally eligible.
public static class RegulatoryEvidence
{
    public const int LookbackDays = 30;
    public const string MethodologyVersion = "reg-v1";

    public static class Tiers
    {
        public const string VeryClose = "very_close";
        public const string Recent = "recent";
        public const string Older = "older";
    }

    public static (DateOnly From, DateOnly To) Window(DateOnly moveDate, int? lookbackDays = null)
    {
        var lookback = lookbackDays is > 0 ? lookbackDays.Value : LookbackDays;
        return (moveDate.AddDays(-lookback), moveDate);
    }

    // Proximity tier by calendar-day distance before the move: 0–1 days
    // very_close, 2–7 recent, 8–30 older (at the default lookback). Null
    // means outside the window (too old, or dated after the move) — never
    // force-fit. Temporal relevance only, never causation.
    public static string? ClassifyTier(DateTime filedAt, DateOnly moveDate, int? lookbackDays = null)
    {
        var lookback = lookbackDays is > 0 ? lookbackDays.Value : LookbackDays;
        var filingDay = DateOnly.FromDateTime(filedAt);
        var days = moveDate.DayNumber - filingDay.DayNumber;
        if (days < 0 || days > lookback)
            return null;
        if (days <= 1)
            return Tiers.VeryClose;
        if (days <= 7)
            return Tiers.Recent;
        return Tiers.Older;
    }

    public static IReadOnlyList<SecFiling> SelectInWindow(
        IEnumerable<SecFiling> filings, DateOnly moveDate, int? lookbackDays = null)
    {
        var (from, to) = Window(moveDate, lookbackDays);
        return filings
            .Where(f => f is not null)
            .Where(f =>
            {
                var day = DateOnly.FromDateTime(f.FiledAt);
                return day >= from && day <= to;
            })
            .OrderByDescending(f => f.FiledAt)
            .ToList();
    }

    public sealed record TierCounts(int VeryClose, int Recent, int Older)
    {
        public int Total => VeryClose + Recent + Older;
    }

    public static TierCounts CountByTier(
        IEnumerable<SecFiling> filings, DateOnly moveDate, int? lookbackDays = null)
    {
        // Counts over the raw input (not pre-filtered): out-of-window rows
        // simply land in no tier, so callers cannot miscount by mistake.
        int veryClose = 0, recent = 0, older = 0;
        foreach (var f in filings)
        {
            if (f is null)
                continue;
            switch (ClassifyTier(f.FiledAt, moveDate, lookbackDays))
            {
                case Tiers.VeryClose: veryClose++; break;
                case Tiers.Recent: recent++; break;
                case Tiers.Older: older++; break;
            }
        }
        return new TierCounts(veryClose, recent, older);
    }

    // Date-only overload for projected DTOs (which carry FiledAt but not the
    // full SecFiling row). Same tier math, same window.
    public static TierCounts CountByTier(
        IEnumerable<DateTime> filedAts, DateOnly moveDate, int? lookbackDays = null)
    {
        int veryClose = 0, recent = 0, older = 0;
        foreach (var filedAt in filedAts)
        {
            switch (ClassifyTier(filedAt, moveDate, lookbackDays))
            {
                case Tiers.VeryClose: veryClose++; break;
                case Tiers.Recent: recent++; break;
                case Tiers.Older: older++; break;
            }
        }
        return new TierCounts(veryClose, recent, older);
    }
}
