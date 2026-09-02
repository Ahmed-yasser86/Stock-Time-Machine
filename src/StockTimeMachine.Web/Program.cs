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
builder.Services.AddHttpClient<IFinnhubRepository, FinnhubRepository>();
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
        builder.Configuration.GetConnectionString("StocksDbConnection"),
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

// ========== MIGRATIONS WITH RETRY ==========
using (var scope = app.Services.CreateScope())
{
    var maxRetries = 5;
    for (int i = 0; i < maxRetries; i++)
    {
        try
        {
            var contactDb = scope.ServiceProvider.GetRequiredService<AppDBContext>();
            await contactDb.Database.MigrateAsync();

            var stocksDb = scope.ServiceProvider.GetRequiredService<StocksDbContext>();
            await stocksDb.Database.MigrateAsync();

            var timeMachineDb = scope.ServiceProvider.GetRequiredService<StockTimeMachineDbContext>();
            await timeMachineDb.Database.EnsureCreatedAsync();
            break;
        }
        catch (Exception ex)
        {
            if (i == maxRetries - 1) throw;
            Console.WriteLine($"Migration attempt {i + 1} failed: {ex.Message}. Retrying in 5s...");
            await Task.Delay(5000);
        }
    }
}

Console.WriteLine("app started");

if (builder.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

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