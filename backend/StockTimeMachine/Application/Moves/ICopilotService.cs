namespace StockTimeMachine;

// Evidence copilot: contextual AI actions over ALREADY-RETRIEVED evidence.
// Same containment contract as cluster briefs (cutoff roleplay, citations,
// no causation/prediction/outside knowledge), plus two harder rules:
// - summarize/translate/suggest/explain ONLY. The copilot never concludes.
// - review NEVER authors: it checks the user's note against cited evidence.
public class NoteIssue
{
    public string Ref { get; set; } = "";
    public string Verdict { get; set; } = "unclear";
    public string Detail { get; set; } = "";
}

public interface ICopilotService
{
    // Summarize the move's filings / contrast selected news / explain the
    // window's uncertainty components in plain words (numbers supplied, no new
    // math) / English gist of non-English threads. Null when AI is off, the
    // evidence is empty, or the model declines.
    Task<ClusterBrief?> SummarizeFilings(string symbol, DateOnly asOfDate, CancellationToken ct = default);
    Task<ClusterBrief?> ContrastArticles(string symbol, DateOnly asOfDate, string? newsSource, IReadOnlyList<string> articleIds, CancellationToken ct = default);
    Task<ClusterBrief?> ExplainUncertainty(string symbol, DateOnly asOfDate, string? newsSource, CancellationToken ct = default);
    Task<ClusterBrief?> GistThread(string symbol, DateOnly asOfDate, string? newsSource, IReadOnlyList<string> articleIds, CancellationToken ct = default);
    // Review the user's conclusion note: one verdict per [ref] claim.
    Task<IReadOnlyList<NoteIssue>> ReviewNote(string symbol, DateOnly asOfDate, string? newsSource, string note, CancellationToken ct = default);
    // Phrase deterministic gap pointers as next steps. The gaps (with their
    // link labels) are supplied by the caller; the model only phrases, never
    // invents routes — every link stays frontend-owned.
    Task<ClusterBrief?> SuggestNextSteps(string symbol, DateOnly asOfDate, string? newsSource, IReadOnlyList<string> gaps, CancellationToken ct = default);
}
