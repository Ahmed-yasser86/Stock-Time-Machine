using Microsoft.AspNetCore.HttpLogging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Serilog;
using StockTimeMachine;
using StockTimeMachine.Web.Integrations;
using StockTimeMachine.Web.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((HostBuilderContext context, IServiceProvider services, LoggerConfiguration loggerConfiguration) =>
{
    loggerConfiguration.ReadFrom.Configuration(context.Configuration)
       .ReadFrom.Services(services);
});

builder.Services.AddHttpClient();
builder.Services.AddControllers();
builder.Services.AddMemoryCache();

builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields = Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.RequestPropertiesAndHeaders |
                            Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.ResponsePropertiesAndHeaders;
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins("http://localhost:3000", "http://localhost:3001", "http://localhost:5173", "http://localhost:4173")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddDbContext<StockTimeMachineDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("StockTimeMachineDb"),
        sqlOptions => sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorNumbersToAdd: null)
    )
);

// SEC EDGAR requires a contact User-Agent on every request. Configured via
// SecEdgar:UserAgent — never hardcoded.
builder.Services.AddHttpClient<ISecEdgarProvider, SecEdgarProvider>((sp, client) =>
{
    var userAgent = sp.GetRequiredService<IConfiguration>()["SecEdgar:UserAgent"]
        ?? "StockTimeMachine/1.0 (contact: your@email.com)";
    // TryParseAdd: an operator-supplied value must be a valid User-Agent.
    // A false result leaves the header unset rather than crashing requests.
    client.DefaultRequestHeaders.UserAgent.TryParseAdd(userAgent);
});
builder.Services.AddHttpClient<IAlphaVantageProvider, AlphaVantageProvider>();
// GDELT can hang: bound the wait so one slow source can never stall an
// investigation past the 504 handling.
builder.Services.AddHttpClient(nameof(GdeltNewsProvider), client =>
{
    client.Timeout = TimeSpan.FromSeconds(25);
});
builder.Services.AddHttpClient(nameof(GdeltCloudNewsProvider), client =>
{
    client.Timeout = TimeSpan.FromSeconds(25);
});

// News providers are singletons holding only long-lived HttpClient instances
// from the factory (no captive-dependency / socket-exhaustion risk). The user
// selects one per investigation via INewsProviderFactory — never mixed,
// never silently substituted.
builder.Services.AddSingleton<GdeltNewsProvider>(sp =>
    new GdeltNewsProvider(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(GdeltNewsProvider)),
        sp.GetRequiredService<ILogger<GdeltNewsProvider>>(),
        sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton<AlphaVantageNewsProvider>(sp =>
    new AlphaVantageNewsProvider(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(AlphaVantageNewsProvider)),
        sp.GetRequiredService<ILogger<AlphaVantageNewsProvider>>(),
        sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton<GdeltCloudNewsProvider>(sp =>
    new GdeltCloudNewsProvider(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(GdeltCloudNewsProvider)),
        sp.GetRequiredService<ILogger<GdeltCloudNewsProvider>>(),
        sp.GetRequiredService<IConfiguration>()));
builder.Services.AddHttpClient(nameof(MarketAuxNewsProvider), client =>
{
    client.Timeout = TimeSpan.FromSeconds(25);
});
builder.Services.AddSingleton<MarketAuxNewsProvider>(sp =>
    new MarketAuxNewsProvider(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(MarketAuxNewsProvider)),
        sp.GetRequiredService<ILogger<MarketAuxNewsProvider>>(),
        sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton<INewsProviderFactory, NewsProviderFactory>();

builder.Services.AddScoped<ICompanyRepository, CompanyRepository>();
builder.Services.AddScoped<IHistoricalDataRepository, HistoricalDataRepository>();
builder.Services.AddSingleton<ICompanyDirectory, JsonCompanyDirectory>();
builder.Services.AddScoped<ITimeMachineService, TimeMachineService>();
builder.Services.AddScoped<ISimulationService, SimulationService>();
builder.Services.AddScoped<IMoveDetectionService, MoveDetectionService>();

// Retail-discussion surface (Arctic Shift: keyless community Reddit archive).
// Best-effort per move; failures degrade to honest per-layer empty states.
builder.Services.AddHttpClient(nameof(ArcticShiftProvider), client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    // Arctic Shift 422s requests without a User-Agent (verified live).
    client.DefaultRequestHeaders.UserAgent.ParseAdd("StockTimeMachine/1.0");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});
builder.Services.AddSingleton<ArcticShiftProvider>(sp =>
    new ArcticShiftProvider(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(ArcticShiftProvider)),
        sp.GetRequiredService<ILogger<ArcticShiftProvider>>(),
        sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton<ISocialSignalProvider>(sp => sp.GetRequiredService<ArcticShiftProvider>());

// Finnhub: self-contained adapters (typed, factory-managed HttpClient).
// Company-profile fallback + delayed live quotes. The token stays
// server-side; browsers only ever receive data, never credentials.
builder.Services.AddHttpClient<ICompanyLookup, FinnhubCompanyLookup>();
builder.Services.AddHttpClient<IQuoteProvider, FinnhubQuoteProvider>();

var app = builder.Build();

if (!builder.Environment.IsEnvironment("Testing"))
{
    using (var scope = app.Services.CreateScope())
    {
        var timeMachineDb = scope.ServiceProvider.GetRequiredService<StockTimeMachineDbContext>();
        await timeMachineDb.Database.EnsureCreatedAsync();
    }
}

// Single error pipeline: the middleware maps domain exceptions to
// RFC 7807 ProblemDetails with the user-facing copy from the user stories.
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseHttpLogging();
app.UseRouting();
app.UseCors("Frontend");

app.MapGet("/", () => Results.Json(new { name = "Stock Time Machine API", version = "1.0", docs = "/api/timemachine/methodology" }));
app.MapGet("/health", () => Results.Ok("Healthy"));

app.MapControllers();

app.Run();

public partial class Program { }
