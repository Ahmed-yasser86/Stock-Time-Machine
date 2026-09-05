using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace StockTimeMachine;

public class InvestigationJobStore : IInvestigationJobStore
{
    private readonly StockTimeMachineDbContext _db;
    private readonly ILogger<InvestigationJobStore> _logger;

    public InvestigationJobStore(StockTimeMachineDbContext db, ILogger<InvestigationJobStore> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<InvestigationJob> CreateAsync(InvestigationJob job, CancellationToken ct = default)
    {
        if (job.CreatedAtUtc == default)
            job.CreatedAtUtc = DateTime.UtcNow;
        job.UpdatedAtUtc = DateTime.UtcNow;
        _db.InvestigationJobs.Add(job);
        await _db.SaveChangesAsync(ct);
        return job;
    }

    public async Task<InvestigationJob?> GetAsync(string id, CancellationToken ct = default) =>
        await _db.InvestigationJobs
            .Include(j => j.Stages)
            .FirstOrDefaultAsync(j => j.Id == id, ct);

    public async Task AppendStageAsync(string id, JobStage stage, CancellationToken ct = default)
    {
        var job = await _db.InvestigationJobs
            .Include(j => j.Stages)
            .FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job is null || JobStatuses.IsTerminal(job.Status))
            return;
        stage.AtUtc = DateTime.UtcNow;
        job.Stages.Add(stage);
        job.UpdatedAtUtc = stage.AtUtc;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> CompleteAsync(string id, string movesJson, string narrativesJson, CancellationToken ct = default)
    {
        var job = await _db.InvestigationJobs.FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job is null || JobStatuses.IsTerminal(job.Status))
            return false;
        job.Status = JobStatuses.Complete;
        job.MovesJson = movesJson;
        job.NarrativesJson = narrativesJson;
        job.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> FailAsync(string id, string status, string? error, CancellationToken ct = default)
    {
        if (!JobStatuses.IsTerminal(status) || status == JobStatuses.Complete)
            throw new ArgumentException("Fail status must be failed, timeout, or cancelled.", nameof(status));
        var job = await _db.InvestigationJobs.FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job is null || JobStatuses.IsTerminal(job.Status))
            return false;
        job.Status = status;
        job.Error = error;
        job.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogWarning("Investigation job {Job} marked {Status}: {Error}", id, status, error);
        return true;
    }

    public async Task ReapStaleAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow - timeout;
        var stale = await _db.InvestigationJobs
            .Where(j => j.Status == JobStatuses.Running && j.CreatedAtUtc < deadline)
            .ToListAsync(ct);
        foreach (var job in stale)
        {
            job.Status = JobStatuses.Timeout;
            job.Error = "Exceeded the 60-minute execution limit.";
            job.UpdatedAtUtc = DateTime.UtcNow;
        }
        if (stale.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogWarning("Reaped {Count} stale investigation jobs as timed out", stale.Count);
        }
    }

    public async Task PruneAsync(TimeSpan retention, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - retention;
        var old = await _db.InvestigationJobs
            .Where(j => j.Status != JobStatuses.Running && j.UpdatedAtUtc < cutoff)
            .ToListAsync(ct);
        if (old.Count == 0)
            return;
        _db.InvestigationJobs.RemoveRange(old);
        await _db.SaveChangesAsync(ct);
    }
}
