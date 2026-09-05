using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using StockTimeMachine;

namespace StockTimeMachine.Tests;

public class InvestigationJobTests
{
    private static StockTimeMachineDbContext NewDb() => new(
        new DbContextOptionsBuilder<StockTimeMachineDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class MapScopeFactory : IServiceScopeFactory
    {
        private readonly Func<IServiceProvider> _provider;
        public MapScopeFactory(Func<IServiceProvider> provider) => _provider = provider;
        public IServiceScope CreateScope() => new MapScope(_provider());
        private sealed class MapScope : IServiceScope
        {
            public MapScope(IServiceProvider provider) => ServiceProvider = provider;
            public IServiceProvider ServiceProvider { get; }
            public void Dispose() { }
        }
    }

    private sealed class MapProvider : IServiceProvider
    {
        private readonly Dictionary<Type, object> _services;
        public MapProvider(Dictionary<Type, object> services) => _services = services;
        public object? GetService(Type serviceType) =>
            _services.TryGetValue(serviceType, out var s) ? s : null;
    }

    private sealed class FastMoves : IMoveDetectionService
    {
        public Task<MovesWindow> GetMoves(string symbol, DateOnly asOfDate, string? newsSource = null, CancellationToken ct = default, IProgress<SnapshotProgress>? progress = null)
        {
            progress?.Report(new SnapshotProgress("detecting", "complete", "1 move", 1));
            return Task.FromResult(new MovesWindow
            {
                CompanySymbol = symbol,
                DecisionDate = asOfDate,
                NewsSource = newsSource ?? NewsSources.Gdelt,
                Summary = new WindowSummary { TradingDays = 100, SufficientHistory = true },
                KeyMoves = new List<KeyMove>
                {
                    new() { Date = asOfDate.AddDays(-1), Close = 100m, DailyReturnPct = 1m },
                },
            });
        }
    }

    private sealed class FastNarratives : INarrativeService
    {
        public Task<NarrativeTopicsResult> GetTopics(string symbol, DateOnly asOfDate, string? newsSource, CancellationToken ct = default, IProgress<SnapshotProgress>? progress = null)
        {
            progress?.Report(new SnapshotProgress("clustering", "complete", "1 thread", 1));
            return Task.FromResult(new NarrativeTopicsResult
            {
                CompanySymbol = symbol,
                AsOfDate = asOfDate,
                NewsSource = newsSource ?? NewsSources.Gdelt,
                ArticlesConsidered = 1,
                ClusteringMethod = "tf-idf-fallback",
                Topics = new List<TopicCluster>
                {
                    new() { LabelTerms = new List<string> { "alpha" }, ArticleIds = new List<string> { "a1" }, RepresentativeTitle = "T" },
                },
            });
        }

        public Task<ClusterBrief?> BriefSharedThread(IReadOnlyList<string> symbols, DateOnly asOfDate, string? newsSource, IReadOnlyList<string> terms, CancellationToken ct = default) =>
            Task.FromResult<ClusterBrief?>(null);

        public Task<IReadOnlyList<CrossThreadPair>> CrossThreadSimilarity(IReadOnlyList<string> symbols, DateOnly asOfDate, string? newsSource, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CrossThreadPair>>(Array.Empty<CrossThreadPair>());
    }

    private sealed class Harness
    {
        public StockTimeMachineDbContext Db { get; } = NewDb();
        public InvestigationJobRunner Runner { get; }
        public InvestigationJobStore Store =>
            new(Db, NullLogger<InvestigationJobStore>.Instance);

        public Harness(IMoveDetectionService moves, INarrativeService narratives, double timeoutMinutes = 60)
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jobs:TimeoutMinutes"] = timeoutMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            }).Build();
            var db = Db;
            var factory = new MapScopeFactory(() => new MapProvider(new Dictionary<Type, object>
            {
                [typeof(IInvestigationJobStore)] = new InvestigationJobStore(db, NullLogger<InvestigationJobStore>.Instance),
                [typeof(IMoveDetectionService)] = moves,
                [typeof(INarrativeService)] = narratives,
            }));
            Runner = new InvestigationJobRunner(factory, config, NullLogger<InvestigationJobRunner>.Instance);
        }

        public async Task<InvestigationJob?> WaitForTerminal(string id, int tries = 100)
        {
            InvestigationJob? job = null;
            for (int i = 0; i < tries && (job is null || !JobStatuses.IsTerminal(job.Status)); i++)
            {
                await Task.Delay(100);
                job = await Store.GetAsync(id);
            }
            return job;
        }
    }

    [Fact]
    public async Task Runner_CompletesAndPersistsPayloads()
    {
        var h = new Harness(new FastMoves(), new FastNarratives());

        var id = await h.Runner.StartAsync("TSLA", new DateOnly(2020, 2, 20), NewsSources.Gdelt);
        var job = await h.WaitForTerminal(id);

        Assert.NotNull(job);
        Assert.Equal(JobStatuses.Complete, job!.Status);
        Assert.Contains("TSLA", job.MovesJson);
        Assert.NotEmpty(job.Stages);
        Assert.Equal(0, h.Runner.RunningCount);
    }

    private sealed class HangingMoves : IMoveDetectionService
    {
        public bool ObservedCancellation { get; private set; }
        public async Task<MovesWindow> GetMoves(string symbol, DateOnly asOfDate, string? newsSource = null, CancellationToken ct = default, IProgress<SnapshotProgress>? progress = null)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
                throw new InvalidOperationException("should have been cancelled");
            }
            catch (OperationCanceledException)
            {
                ObservedCancellation = true;
                throw;
            }
        }
    }

    private sealed class ThrowingMoves : IMoveDetectionService
    {
        public Task<MovesWindow> GetMoves(string symbol, DateOnly asOfDate, string? newsSource = null, CancellationToken ct = default, IProgress<SnapshotProgress>? progress = null) =>
            throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task Runner_Timeout_TerminatesPipeline()
    {
        var moves = new HangingMoves();
        var h = new Harness(moves, new FastNarratives(), timeoutMinutes: 0.005); // ~0.3s

        var id = await h.Runner.StartAsync("TSLA", new DateOnly(2020, 2, 20), NewsSources.Gdelt);
        Assert.Equal(1, h.Runner.RunningCount);
        var job = await h.WaitForTerminal(id);

        Assert.NotNull(job);
        Assert.Equal(JobStatuses.Timeout, job!.Status);
        Assert.True(moves.ObservedCancellation, "pipeline task must actually observe cancellation");
        Assert.Equal(0, h.Runner.RunningCount);
    }

    [Fact]
    public async Task Runner_Failure_PersistsError()
    {
        var h = new Harness(new ThrowingMoves(), new FastNarratives());

        var id = await h.Runner.StartAsync("TSLA", new DateOnly(2020, 2, 20), NewsSources.Gdelt);
        var job = await h.WaitForTerminal(id);

        Assert.NotNull(job);
        Assert.Equal(JobStatuses.Failed, job!.Status);
        Assert.Contains("boom", job.Error);
        Assert.Equal(0, h.Runner.RunningCount);
    }

    [Fact]
    public async Task Store_TerminalTransitions_AreCompareAndSwap()
    {
        var h = new Harness(new FastMoves(), new FastNarratives());
        var created = await h.Store.CreateAsync(new InvestigationJob
        {
            CompanySymbol = "TSLA",
            DecisionDate = new DateOnly(2020, 2, 20),
            NewsSource = NewsSources.Gdelt,
            Status = JobStatuses.Running,
        });

        Assert.True(await h.Store.CompleteAsync(created.Id, "{}", "{}"));
        Assert.False(await h.Store.CompleteAsync(created.Id, "{}", "{}"));
        Assert.False(await h.Store.FailAsync(created.Id, JobStatuses.Failed, "late"));
        var job = await h.Store.GetAsync(created.Id);
        Assert.Equal(JobStatuses.Complete, job!.Status);
    }

    [Fact]
    public async Task Store_ReapsStaleRunningJobs()
    {
        var h = new Harness(new FastMoves(), new FastNarratives());
        var created = await h.Store.CreateAsync(new InvestigationJob
        {
            CompanySymbol = "TSLA",
            DecisionDate = new DateOnly(2020, 2, 20),
            NewsSource = NewsSources.Gdelt,
            Status = JobStatuses.Running,
            CreatedAtUtc = DateTime.UtcNow - TimeSpan.FromHours(2),
        });

        await h.Store.ReapStaleAsync(TimeSpan.FromMinutes(60));

        var job = await h.Store.GetAsync(created.Id);
        Assert.Equal(JobStatuses.Timeout, job!.Status);
    }

    [Fact]
    public async Task Runner_ConcurrentJobs_AllComplete()
    {
        var h = new Harness(new FastMoves(), new FastNarratives());

        var ids = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            h.Runner.StartAsync("TSLA", new DateOnly(2020, 2, 20), NewsSources.Gdelt)));
        Assert.Equal(5, ids.Distinct().Count());
        foreach (var id in ids)
        {
            var job = await h.WaitForTerminal(id);
            Assert.Equal(JobStatuses.Complete, job!.Status);
        }
        Assert.Equal(0, h.Runner.RunningCount);
    }
}
