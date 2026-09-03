namespace StockTimeMachine;

// One honest reconstruction step for US-06 live progress. States:
// "started" | "complete" | "partial" | "failed" | "skipped".
// "skipped" marks rescoped-out sections (client choice, not a gap).
// A failed step never shows a success state; partial notes limited coverage.
public sealed record SnapshotProgress(string Stage, string State, string? Detail = null, int? Count = null)
{
    public const string Started = "started";
    public const string Complete = "complete";
    public const string Partial = "partial";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
}

// Canonical stage keys. Labels live in the frontend; keys are the contract.
// "outcome" extends the US-06 seven with the subsequent-outcome evaluation.
public static class SnapshotStages
{
    public const string Company = "company";
    public const string Prices = "prices";
    public const string Boundary = "boundary";
    public const string Filings = "filings";
    public const string Disclosures = "disclosures";
    public const string News = "news";
    public const string Outcome = "outcome";
    public const string Assembly = "assembly";

    public static readonly IReadOnlyList<string> AllInOrder = new[]
    {
        Company, Prices, Boundary, Filings, Disclosures, News, Outcome, Assembly
    };
}
