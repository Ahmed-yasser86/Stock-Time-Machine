using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using StockTimeMachine;

namespace StockTimeMachine.Tests;

public class ApiContractTests : IClassFixture<ApiContractTests.Factory>
{
    private readonly Factory _factory;

    public ApiContractTests(Factory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/timemachine/health");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task CompanySearch_KnownQuery_ReturnsMatches()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/timemachine/company-search?q=apple");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var data = await resp.Content.ReadFromJsonAsync<List<CompanyResult>>();
        Assert.NotNull(data);
        Assert.NotEmpty(data!);
        Assert.Contains(data!, c => c.symbol == "AAPL");
    }

    [Fact]
    public async Task CompanySearch_EmptyQuery_ReturnsEmptyArray()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/timemachine/company-search?q=");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var data = await resp.Content.ReadFromJsonAsync<List<CompanyResult>>();
        Assert.NotNull(data);
        Assert.Empty(data!);
    }

    [Fact]
    public async Task Snapshot_MissingSymbol_Returns400()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/timemachine/snapshot?date=2020-01-15");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Snapshot_InvalidDate_Returns400()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/timemachine/snapshot?symbol=TSLA&date=not-a-date");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Snapshot_FutureDate_Returns400()
    {
        var client = _factory.CreateClient();
        var future = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2);
        var resp = await client.GetAsync($"/api/timemachine/snapshot?symbol=TSLA&date={future:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Snapshot_RescopedSections_OmitsUnrequestedWarnings()
    {
        // ZZZZ resolves to a shell company with no CIK: no provider is callable,
        // so this test is network-free and deterministic.
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/timemachine/snapshot?symbol=ZZZZ&date=2020-01-15&sections=prices");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var data = await resp.Content.ReadFromJsonAsync<SnapshotWarnings>();
        Assert.NotNull(data);
        Assert.Single(data!.warnings);
        Assert.Contains("not available", data.warnings[0]);
    }

    [Fact]
    public async Task Snapshot_MarketAuxWithoutKey_ReturnsHonestEmpty()
    {
        // Testing env has no MarketAux key: provider short-circuits before HTTP,
        // so this test is network-free and deterministic.
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/timemachine/snapshot?symbol=ZZZZ&date=2020-01-15&newsSource=marketaux");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var data = await resp.Content.ReadFromJsonAsync<SnapshotWarnings>();
        Assert.NotNull(data);
        Assert.Contains(data!.warnings, w => w.Contains("MarketAux"));
    }

    [Fact]
    public async Task SnapshotStream_ReturnsStagesThenSnapshot()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/timemachine/snapshot/stream?symbol=ZZZZ&date=2020-01-15&sections=prices");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("text/event-stream", resp.Content.Headers.ContentType?.MediaType);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("event: stage", body);
        Assert.Contains("event: snapshot", body);
        Assert.Contains("\"symbol\":\"ZZZZ\"", body);
    }

    [Fact]
    public async Task Moves_UnknownSymbol_ReturnsOkEmpty()
    {
        // ZZZZ has no CIK and empty test DB: deterministic, network-free.
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/timemachine/moves?symbol=ZZZZ&date=2020-01-15");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("sufficientHistory", body);
        Assert.Contains("false", body);
        Assert.Contains("uncertainty", body);
        Assert.Contains("evidence-sparsity", body);
        Assert.Contains("uncertainty", body);
        Assert.Contains("evidence-sparsity", body);
    }

    [Fact]
    public async Task Moves_MissingSymbol_Returns400()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/timemachine/moves?date=2020-01-15");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Moves_InvalidDate_Returns400()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/timemachine/moves?symbol=TSLA&date=not-a-date");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Snapshot_UnknownSection_Returns400()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/timemachine/snapshot?symbol=TSLA&date=2020-01-15&sections=bogus");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Methodology_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/timemachine/methodology");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Methodology_DocumentsMoveWeights()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/timemachine/methodology");
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("Key Moves", body);
        Assert.Contains("0.5", body);
        Assert.Contains("0.3", body);
        Assert.Contains("0.2", body);
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                ReplaceDbContext<StockTimeMachineDbContext>(services);
            });
        }

        private static void ReplaceDbContext<T>(IServiceCollection services) where T : DbContext
        {
            // The diagnostic showed both SqlServer and InMemory extensions ending up
            // in one resolved options instance, so instead of relying on AddDbContext
            // (TryAdd semantics), register the exact options instance deterministically:
            // plain AddScoped always wins over earlier registrations.
            var stale = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<T>) ||
                            d.ServiceType == typeof(DbContextOptions) ||
                            d.ServiceType == typeof(T))
                .ToList();
            foreach (var d in stale) services.Remove(d);

            services.AddScoped(_ => new DbContextOptionsBuilder<T>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
            services.AddScoped<T>();
        }
    }

    private sealed record CompanyResult(string symbol, string name, string cik, string exchange, string sector);
    private sealed record SnapshotWarnings(List<string> warnings);
}