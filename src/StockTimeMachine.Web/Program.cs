using Entities;
using Microsoft.EntityFrameworkCore;
using Repositories;
using RepositoryContracts;
using RepositryContracts;
using Serilog;
using ServiceContracts;
using Services;
using Servicess;
using RepositoryContracts;
using Repositories_Stocks;
using StocksApp2;
using ServiceContractsContacts;
using EntitiesStocks;
using Entities;

using StockTimeMachine.Entities;
using StockTimeMachine.Repositories;
using StockTimeMachine.RepositoryContracts;
using StockTimeMachine.ProviderContracts;
using StockTimeMachine.Providers;
using StockTimeMachine.ServiceContracts;
using StockTimeMachine.Services;
using StockTimeMachine.Web.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((HostBuilderContext context, IServiceProvider services, LoggerConfiguration loggerConfiguration) =>
{
    loggerConfiguration.ReadFrom.Configuration(context.Configuration)
       .ReadFrom.Services(services);
});

builder.Services.AddHttpClient();
builder.Services.AddControllersWithViews();

// ========== REPOSITORY LAYER ==========
builder.Services.AddScoped<CountryRepositryContract, CountryRepository>();
builder.Services.AddScoped<PersonRepositryContract, PersonRepository>();

// ========== SERVICE LAYER ==========
builder.Services.AddScoped<ICountryServices, CountryServices>();
builder.Services.AddScoped<IPersonServices, PersonServices>();

// ========== FINNHUB (NO DATABASE) ==========
builder.Services.AddScoped<IFinnhubRepository, FinnhubRepository>();
builder.Services.AddScoped<IFinnhubService, FinnhubService>();

// ========== STOCKS (WITH DATABASE) ==========
builder.Services.AddScoped<IStocksRepository, StocksRepository>();
builder.Services.AddScoped<IStocksService, StocksService>();

// ========== DATABASE CONTEXTS ==========
builder.Services.AddDbContext<AppDBContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("ContactDb"),
        sqlOptions => sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorNumbersToAdd: null)
    )
);

builder.Services.AddDbContext<StocksDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("StocksDbConnection"),
        sqlOptions =>
        {
            sqlOptions.MigrationsAssembly("EntitiesStocks");
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null);
        }
    )
);

builder.Services.AddDbContext<StockTimeMachineDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("StockTimeMachineDb"),
        sqlOptions => sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorNumbersToAdd: null)
    )
);

// ========== STOCK TIME MACHINE ==========
builder.Services.AddHttpClient<ISecEdgarProvider, SecEdgarProvider>(client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", "StockTimeMachine/1.0 (research@example.com)");
});
builder.Services.AddHttpClient<IAlphaVantageProvider, AlphaVantageProvider>();
builder.Services.AddScoped<ICompanyRepository, CompanyRepository>();
builder.Services.AddScoped<IHistoricalDataRepository, HistoricalDataRepository>();
builder.Services.AddScoped<ITimeMachineService, TimeMachineService>();
builder.Services.AddScoped<ISimulationService, SimulationService>();

// ========== CONFIGURATION ==========
builder.Services.Configure<TradingOptions>(
    builder.Configuration.GetSection("TradingOptions"));

// ========== HTTP LOGGING ==========
builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields = Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.RequestPropertiesAndHeaders |
                            Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.ResponsePropertiesAndHeaders;
});

var app = builder.Build();

// ========== DATABASE INIT ==========
using (var scope = app.Services.CreateScope())
{
    var contactDb = scope.ServiceProvider.GetRequiredService<AppDBContext>();
    await contactDb.Database.EnsureCreatedAsync();

    var stocksDb = scope.ServiceProvider.GetRequiredService<StocksDbContext>();
    await stocksDb.Database.EnsureCreatedAsync();

    var timeMachineDb = scope.ServiceProvider.GetRequiredService<StockTimeMachineDbContext>();
    await timeMachineDb.Database.EnsureCreatedAsync();
}

Console.WriteLine("app started");

if (builder.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseHttpLogging();
app.UseRouting();
app.UseStaticFiles();

// ========== HEALTH CHECK ==========
app.MapGet("/health", () => Results.Ok("Healthy"));

app.MapControllers();

app.MapControllerRoute(
    name: "MyAreas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Trade}/{action=Index}/{id?}");

app.Run();

public partial class Program { }