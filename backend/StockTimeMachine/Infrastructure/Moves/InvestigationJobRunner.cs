using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace StockTimeMachine;

// Background investigation runs, decoupled from any client connection.
// - Persisted BEFORE the pipeline starts: disconnects never stop the job.
// - Hard timeout from Start (60 minutes default): OCE becomes `timeout`,
//   terminal and persisted; resources disposed via the continuation.
// - Synchronous progress fan-in: stage order in the store matches emission
//   order (Progress<T> would scramble it across threads).
// - Terminal jobs leave the live map; rows stay queryable until pruned.
public class InvestigationJobRunner : IInvestigationJobRunner
{
    private sealed class SyncProgress : IProgress<SnapshotProgress>
    {
        private readonly Action<SnapshotProgress> _report;
        public SyncProgress(Action<SnapshotProgress> report) => _report = report;
        public void Report(SnapshotProgress value) => _report(value);
    }

    private readonly IServiceScopeFactory _scopes;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _retention;
    private readonly ILogger<InvestigationJobRunner> _logger;
    private readonly ConcurrentDictionary<string, (Task Task, CancellationTokenSource Cts)> _running = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public int RunningCount => _running.Count;
    public TimeSpan Timeout => _timeout;

    public InvestigationJobRunner(IServiceScopeFactory scopes, IConfiguration config, ILogger<InvestigationJobRunner> logger)
    {
        _scopes = scopes;
        var minutes = double.TryParse(config["Jobs:TimeoutMinutes"],
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var m) && m > 0 ? m : 60;
        _timeout = TimeSpan.FromMinutes(minutes);
        var days = double.TryParse(config["Jobs:RetentionDays"],
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var d) && d > 0 ? d : 7;
        _retention = TimeSpan.FromDays(days);
        _logger = logger;
    }

    public async Task<string> StartAsync(string symbol, DateOnly asOfDate, string newsSource, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new InvalidHistoricalDateException("Symbol is required.");
        HistoricalDate.Create(asOfDate);
        var normalized = symbol.Trim().ToUpperInvariant();
        var selected = NewsSources.Normalize(newsSource);

        using (var scope = _scopes.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IInvestigationJobStore>();
            var job = await store.CreateAsync(new InvestigationJob
            {
                CompanySymbol = normalized,
                DecisionDate = asOfDate,
                NewsSource = selected,
                Status = JobStatuses.Running,
            }, ct);
            await store.PruneAsync(_retention, ct);

            var cts = new CancellationTokenSource(_timeout);
            var task = RunPipelineAsync(job.Id, normalized, asOfDate, selected, cts.Token);
            _running[job.Id] = (task, cts);
            _ = task.ContinueWith(_ =>
            {
                if (_running.TryRemove(job.Id, out var entry))
                    entry.Cts.Dispose();
            }, TaskScheduler.Default);
            _logger.LogInformation("Investigation job {Job} started for {Symbol} (timeout {Timeout})",
                job.Id, normalized, _timeout);
            return job.Id;
        }
    }

    private async Task RunPipelineAsync(string id, string symbol, DateOnly asOfDate, string newsSource, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IInvestigationJobStore>();
        var moves = scope.ServiceProvider.GetRequiredService<IMoveDetectionService>();
        var narratives = scope.ServiceProvider.GetRequiredService<INarrativeService>();
        var progress = new SyncProgress(stage =>
            store.AppendStageAsync(id, new JobStage
            {
                Stage = stage.Stage,
                State = stage.State,
                Detail = stage.Detail,
                Count = stage.Count,
            }, CancellationToken.None).GetAwaiter().GetResult());
        try
        {
            var window = await moves.GetMoves(symbol, asOfDate, newsSource, ct, progress);
            var topics = await narratives.GetTopics(symbol, asOfDate, newsSource, ct, progress);
            var done = await store.CompleteAsync(id,
                JsonSerializer.Serialize(window, Json),
                JsonSerializer.Serialize(topics, Json),
                CancellationToken.None);
            _logger.LogInformation("Investigation job {Job} completed (stored: {Stored})", id, done);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await store.FailAsync(id, JobStatuses.Timeout,
                "Exceeded the 60-minute execution limit.", CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Investigation job {Job} failed", id);
            await store.FailAsync(id, JobStatuses.Failed, ex.Message, CancellationToken.None);
        }
    }
}
