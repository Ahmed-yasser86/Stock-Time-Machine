namespace StockTimeMachine;

// Named, repeating pre-peak pattern. The catalog is static code (v1): adding
// a signal is adding a definition + a predicate in HypeSignals — no schema
// change, no migration. Deterministic triggers only (flags, categories,
// label terms, regime path, sentiment); LLM briefs narrate, embeddings
// assist recall — neither detects.
public class HypeSignalDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    // Plain-English trigger description (shown in UI + methodology).
    public string Trigger { get; set; } = "";
    // All catalog signals describe pre-peak information conditions, never
    // move direction (Issue 6): a signal may fire on an up-spike or a
    // plunge alike. The UI renders DirectionalNote wherever the name shows.
    public bool NonDirectional { get; set; } = true;
    public string DirectionalNote { get; set; } =
        "Describes pre-peak information conditions, not move direction.";
    public string Version { get; set; } = HypeSignalCatalog.Version;
}

// One fired trigger on one case, with concrete evidence references
// (thread titles, flags, regime dates) — never a bare label.
public class TriggerEvidenceItem
{
    // Full display line, including the Issue 2 basis suffix.
    public string Text { get; set; } = "";
    // Thread metadata so users weight evidence themselves (Issue 7):
    // member-article count, relevance rate, thread category + basis.
    // All default (0/null/"") on non-thread lines (regime spans, flags).
    public int ThreadSize { get; set; }
    public double? RelevanceRate { get; set; }
    public string Category { get; set; } = "";
    public string CategoryBasis { get; set; } = "";
    // Majority-vote rationale stored on the thread (counts + basis); empty
    // on legacy rows. Rendered next to the basis chip so the next Bondi
    // arrives pre-flagged instead of discovered by users.
    public string CategoryRationale { get; set; } = "";
    // String form for text-only consumers (brief prompts, logs).
    public string RenderedText => Text;
}

public class HypeSignalMatch
{
    public string SignalId { get; set; } = "";
    public string Name { get; set; } = "";
    public List<TriggerEvidenceItem> TriggerEvidence { get; set; } = new();
    // Article ids behind the trigger (for resemblance joins). Additive
    // (reason: Step 4 resemblance needs vectors, not prose).
    public List<string> TriggerThreadIds { get; set; } = new();
}

public static class HypeSignalCatalog
{
    public const string Version = "hs-v1";

    public static readonly IReadOnlyList<HypeSignalDefinition> All = new List<HypeSignalDefinition>
    {
        new() { Id = "earnings-chatter", Name = "Earnings-chatter clustering",
            Trigger = "2+ FINANCIAL-category threads in the 20 days before the peak." },
        new() { Id = "regulatory-overhang", Name = "Regulatory overhang",
            Trigger = "LEGAL/REGULATORY thread before the peak plus a tense regime on 3+ pre-peak days." },
        new() { Id = "volume-first-divergence", Name = "Volume-first divergence",
            Trigger = "High-volume spike/plunge on thin narrative (≤2 pre-peak news items)." },
        new() { Id = "leadership-turbulence", Name = "Leadership turbulence",
            Trigger = "MANAGEMENT-category thread in the 20 days before the peak." },
        new() { Id = "sentiment-split", Name = "Sentiment split",
            Trigger = "Scored news leans against the price move (contrarian divergence)." },
        new() { Id = "supply-tremor", Name = "Supply-chain tremor",
            Trigger = "SUPPLY_CHAIN thread before the peak plus a warming→tense regime shift." },
    }.AsReadOnly();

    public static HypeSignalDefinition? ById(string id) =>
        All.FirstOrDefault(s => s.Id == id);
}
