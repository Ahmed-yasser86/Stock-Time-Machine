namespace StockTimeMachine;

// "This peak resembles past peaks": max-pairwise embedding cosine between the
// triggering threads' article vectors and each library case's thread vectors.
// Recall aid only — resemblance scores assist, never trigger (see
// HypeSignals). Cache-only vectors on both sides: no fresh embedding spend,
// no quota; cases without cached vectors are skipped explicitly.
// Record (not class): the merge step derives labeled copies via `with`
// (reason: dual-query merge tags strong/pattern/narrative without mutating).
public record HypeCaseResemblance
{
    public string CaseId { get; set; } = "";
    public string Symbol { get; set; } = "";
    public DateOnly PeakDate { get; set; }
    // Source provenance of the matched registry case (stamped from the
    // registry row at query time — vector payloads carry no source, and
    // resemblance joins the whole registry by design). Empty when the
    // vector hit has no registry row (unknown provenance, never assumed).
    public string NewsSource { get; set; } = "";
    public double Similarity { get; set; }
    // Which layer produced the match: "pattern" (hype_cases) or "narrative"
    // (hype_threads). Shown as a badge so the resemblance story is auditable.
    public string Kind { get; set; } = HypeResemblanceKinds.Narrative;
}

public static class HypeResemblanceKinds
{
    public const string Pattern = "pattern";
    public const string Narrative = "narrative";
    // Both structural and hybrid queries agree on the case.
    public const string Strong = "strong";

    // Structural-dominant signals match on pattern regardless of news topic;
    // content-dominant signals need the hybrid (topic-sensitive) query.
    public static readonly ISet<string> StructuralSignals = new HashSet<string>(StringComparer.Ordinal)
    {
        "regulatory-overhang",
        "leadership-turbulence",
        "volume-first-divergence",
    };
}

public interface IHypeResemblanceService
{
    Task<IReadOnlyList<HypeCaseResemblance>> FindResemblingAsync(
        HypeCaseDetail current,
        IReadOnlyList<string> triggerArticleIds,
        IReadOnlyList<HypeCase> library,
        CancellationToken ct = default);

    // Full hybrid query vector for one case (structural + content mean),
    // shared by live matching and the distribution endpoint so both measure
    // the same thing. Null when the case has no measurable content at all.
    Task<float[]?> BuildCaseQueryAsync(HypeCaseDetail detail, CancellationToken ct = default);
}
