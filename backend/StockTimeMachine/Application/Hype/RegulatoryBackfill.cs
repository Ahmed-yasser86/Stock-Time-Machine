namespace StockTimeMachine;

// Regulatory methodology migration (reg-v1) for frozen payloads: the
// operator backfill endpoint recomputes movement-level regulatory evidence
// under the 30-day window from data already stored (no provider calls).
// Pure and deterministic; the controller owns IO, this owns the math —
// which is why it is unit-testable here instead of behind the endpoint.
// Idempotent: clean rows are detected (IsRegulatoryClean) and skipped, and
// both migrate methods report whether anything changed so reruns are cheap.
public static class RegulatoryBackfill
{
    // Clean = already under reg-v1 (version stamped, tiers present) with no
    // out-of-window filing in the frozen evidence.
    public static bool IsRegulatoryClean(HypeCaseDetail detail) =>
        detail.RegulatoryMethodology == RegulatoryEvidence.MethodologyVersion &&
        detail.RegulatoryTiers.Count == 3 &&
        !detail.Evidence.Filings.Any(f =>
            RegulatoryEvidence.ClassifyTier(f.FiledAt, detail.PeakDate) is null);

    // Recompute the regulatory slice of one frozen case in place: window the
    // frozen filings, refresh arrival + tiers + provenance. Returns true when
    // anything changed.
    public static bool MigrateCaseDetail(HypeCaseDetail detail)
    {
        var windowed = detail.Evidence.Filings
            .Where(f => RegulatoryEvidence.ClassifyTier(f.FiledAt, detail.PeakDate) is not null)
            .OrderByDescending(f => f.FiledAt)
            .ToList();
        var doctor = new MoveEvidence
        {
            Filings = windowed.Select(f => new SecFiling
            {
                AccessionNumber = f.AccessionNumber ?? "",
                FormType = f.FormType ?? "",
                FiledAt = f.FiledAt,
                Url = f.Url ?? "",
                CompanySymbol = detail.CompanySymbol,
            }).ToList(),
            News = detail.Evidence.News
                .Select(n => new NewsArticle { Title = n.Title ?? "", PublishedAt = n.PublishedAt })
                .ToList(),
            Social = detail.Evidence.Social
                .Select(s => new SocialSignal { CreatedAt = s.CreatedAt })
                .ToList(),
        };
        var arrival = ArrivalMap.Build(detail.PeakDate, doctor);
        var tiers = RegulatoryEvidence.CountByTier(
            windowed.Select(f => f.FiledAt), detail.PeakDate);
        bool changed =
            windowed.Count != detail.Evidence.Filings.Count ||
            detail.RegulatoryMethodology != RegulatoryEvidence.MethodologyVersion ||
            !arrival.Select(a => (a.Layer, a.State, a.FirstSeen, a.Detail))
                .SequenceEqual(detail.Evidence.Arrival.Select(a => (a.Layer, a.State, a.FirstSeen, a.Detail)));
        detail.Evidence.Filings = windowed;
        detail.Evidence.NewsCount = detail.Evidence.News.Count;
        detail.Evidence.FilingCount = windowed.Count;
        detail.Evidence.SocialCount = detail.Evidence.Social.Count;
        detail.Evidence.NewsTitles = detail.Evidence.News.Select(n => n.Title ?? "")
            .Where(t => t.Length > 0).ToList();
        detail.Evidence.Arrival = arrival.Select(a => new HypeCaseArrival
        {
            Layer = a.Layer,
            FirstSeen = a.FirstSeen,
            State = a.State,
            LagHours = a.LagHours,
            Detail = a.Detail ?? "",
        }).ToList();
        detail.RegulatoryLookbackDays = RegulatoryEvidence.LookbackDays;
        detail.RegulatoryMethodology = RegulatoryEvidence.MethodologyVersion;
        detail.RegulatoryTiers = new Dictionary<string, int>
        {
            [RegulatoryEvidence.Tiers.VeryClose] = tiers.VeryClose,
            [RegulatoryEvidence.Tiers.Recent] = tiers.Recent,
            [RegulatoryEvidence.Tiers.Older] = tiers.Older,
        };
        return changed;
    }

    // Recompute one frozen moves window in place. Returns true when any
    // move's filings or arrival changed.
    public static bool MigrateMovesWindow(MovesWindow window)
    {
        var changed = false;
        foreach (var move in window.KeyMoves)
        {
            if (!window.EvidenceByDate.TryGetValue(move.Date.ToString("yyyy-MM-dd"), out var evidence) ||
                evidence is null)
                continue;
            var beforeFilings = evidence.Filings.Count;
            var beforeArrival = evidence.Arrival
                .Select(a => (a.Layer, a.State, a.FirstSeen, a.Detail)).ToList();
            evidence.Filings = RegulatoryEvidence
                .SelectInWindow(evidence.Filings, move.Date)
                .ToList();
            evidence.Arrival = ArrivalMap.Build(move.Date, evidence);
            if (evidence.Filings.Count != beforeFilings ||
                !evidence.Arrival.Select(a => (a.Layer, a.State, a.FirstSeen, a.Detail))
                    .SequenceEqual(beforeArrival))
                changed = true;
        }
        return changed;
    }
}
