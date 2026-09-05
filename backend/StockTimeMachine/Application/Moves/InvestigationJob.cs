namespace StockTimeMachine;

// Persisted background investigation run. Lifecycle contract:
// - persisted + client disconnects → job KEEPS running, reattach by id;
// - never persisted (inline stream) + disconnect → RequestAborted cancels;
// - runtime > JobTimeout → terminated, marked timeout, never resumes.
// Only terminal states (complete/failed/timeout/cancelled) are final, and
// only a running job may transition (CAS in the store update).
public class InvestigationJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CompanySymbol { get; set; } = "";
    public DateOnly DecisionDate { get; set; }
    public string NewsSource { get; set; } = NewsSources.Gdelt;
    public string Status { get; set; } = JobStatuses.Running;
    public List<JobStage> Stages { get; set; } = new();
    public string? MovesJson { get; set; }
    public string? NarrativesJson { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class JobStage
{
    public string Stage { get; set; } = "";
    public string State { get; set; } = "";
    public string? Detail { get; set; }
    public int? Count { get; set; }
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
}

public static class JobStatuses
{
    public const string Running = "running";
    public const string Complete = "complete";
    public const string Failed = "failed";
    public const string Timeout = "timeout";
    public const string Cancelled = "cancelled";

    public static bool IsTerminal(string status) =>
        status == Complete || status == Failed || status == Timeout || status == Cancelled;
}
