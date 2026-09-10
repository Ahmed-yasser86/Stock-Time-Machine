using System.Reflection;
using Xunit;
using StockTimeMachine;
using StockTimeMachine.Integrations;

namespace StockTimeMachine.Tests;

// Guards docs/architecture.md §1 and §8: layering and legacy isolation are
// structural properties, enforced here so no future change can silently
// re-couple the product to the quarantined backend/legacy/ projects.
public class ArchitectureTests
{
    // Every legacy assembly simple name, past and present. If a new legacy
    // project is quarantined, add its name here.
    private static readonly string[] LegacyAssemblies =
    {
        "Entities", "EntitiesStocks",
        "Services", "Servicess",
        "ServiceContracts", "ServiceContractsContacts",
        "Repositories", "Repositories_Stocks",
        "RepositoryContracts", "RepositryContracts", "RipositoryContracts", "RepositeryContracts",
    };

    private static string[] ReferencedNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name ?? "").ToArray();

    [Fact]
    public void Core_DoesNotReferenceLegacyAssemblies()
    {
        var referenced = ReferencedNames(typeof(HistoricalSnapshot).Assembly);
        var violations = referenced.Intersect(LegacyAssemblies, StringComparer.Ordinal).ToArray();
        Assert.True(violations.Length == 0,
            $"StockTimeMachine references quarantined assemblies: {string.Join(", ", violations)}");
    }

    [Fact]
    public void Web_DoesNotReferenceLegacyProjects()
    {
        var referenced = ReferencedNames(typeof(Program).Assembly);
        var violations = referenced.Intersect(LegacyAssemblies, StringComparer.Ordinal).ToArray();
        Assert.True(violations.Length == 0,
            $"StockTimeMachine.Web references quarantined assemblies: {string.Join(", ", violations)}");
    }

    [Fact]
    public void Core_DoesNotDependOnAspNetCore()
    {
        // US-28: the engine must stay callable without web infrastructure.
        var referenced = ReferencedNames(typeof(HistoricalSnapshot).Assembly);
        var violations = referenced.Where(n => n.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)).ToArray();
        Assert.True(violations.Length == 0,
            $"StockTimeMachine references ASP.NET Core: {string.Join(", ", violations)}");
    }

    [Fact]
    public void Repository_PortsStaySegregated()
    {
        // ISP split: one implementation, four focused ports + the composite.
        Assert.True(typeof(IPriceRepository).IsAssignableFrom(typeof(HistoricalDataRepository)));
        Assert.True(typeof(IFilingRepository).IsAssignableFrom(typeof(HistoricalDataRepository)));
        Assert.True(typeof(INewsRepository).IsAssignableFrom(typeof(HistoricalDataRepository)));
        Assert.True(typeof(IAiCacheRepository).IsAssignableFrom(typeof(HistoricalDataRepository)));
        Assert.True(typeof(IHistoricalDataRepository).IsAssignableTo(typeof(IPriceRepository)));
        Assert.True(typeof(IHistoricalDataRepository).IsAssignableTo(typeof(IFilingRepository)));
        Assert.True(typeof(IHistoricalDataRepository).IsAssignableTo(typeof(INewsRepository)));
        Assert.True(typeof(IHistoricalDataRepository).IsAssignableTo(typeof(IAiCacheRepository)));
    }

    [Fact]
    public void MoveHelpers_StayPureStatics()
    {
        // MoveScorer / WindowStatistics must stay side-effect-free statics;
        // MoveDetectionService stays the orchestrator (instance, DI-built).
        Assert.True(typeof(MoveScorer).IsAbstract && typeof(MoveScorer).IsSealed);
        Assert.True(typeof(WindowStatistics).IsAbstract && typeof(WindowStatistics).IsSealed);
        Assert.False(typeof(MoveDetectionService).IsAbstract && typeof(MoveDetectionService).IsSealed);
    }

    [Fact]
    public void FinnhubAdapters_StayOutsideWeb()
    {
        // Infrastructure adapters must not live in the presentation layer.
        Assert.Equal("StockTimeMachine.Integrations", typeof(FinnhubQuoteProvider).Namespace);
        Assert.Equal("StockTimeMachine.Integrations", typeof(FinnhubCompanyLookup).Namespace);
    }

    [Fact]
    public void RelevanceService_DependsOnRangeSearchAbstraction()
    {
        // Expansion capability must come through the Application port, never
        // the concrete transport: pinning the DIP direction so a future
        // transport can serve expansion without touching RelevanceService.
        var ctors = typeof(RelevanceService).GetConstructors();
        var ctor = Assert.Single(ctors);
        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType).ToArray();
        Assert.Contains(typeof(IRangeNewsSearcher), paramTypes);
        Assert.DoesNotContain(paramTypes, t => t == typeof(GdeltNewsProvider));
        Assert.True(typeof(IRangeNewsSearcher).IsAssignableFrom(typeof(GdeltNewsProvider)));
    }

    [Fact]
    public void PersistencePorts_AreAdopted()
    {
        // Every repository consumer depends only on the focused port(s) it
        // uses. The composite IHistoricalDataRepository is a compat seam:
        // no constructor may take it directly.
        var consumers = new[]
        {
            typeof(TimeMachineService),
            typeof(SimulationService),
            typeof(MoveDetectionService),
            typeof(NarrativeService),
            typeof(CopilotService),
            typeof(RelevanceService),
            typeof(FinBertSentimentAnalyzer),
            typeof(HypeBriefService),
            typeof(HypeCaseIndexer),
            typeof(HypeFilingService),
            typeof(HypeResemblanceService),
        };
        var focused = new[]
        {
            typeof(ICompanyRepository),
            typeof(IPriceRepository),
            typeof(IFilingRepository),
            typeof(INewsRepository),
            typeof(IAiCacheRepository),
        };
        foreach (var consumer in consumers)
        {
            var ctor = Assert.Single(consumer.GetConstructors());
            var paramTypes = ctor.GetParameters().Select(p => p.ParameterType).ToArray();
            Assert.DoesNotContain(typeof(IHistoricalDataRepository), paramTypes);
            var repoParams = paramTypes.Where(t => t.Name.EndsWith("Repository", StringComparison.Ordinal)).ToArray();
            Assert.NotEmpty(repoParams);
            Assert.All(repoParams, t => Assert.Contains(t, focused));
        }
    }
}
