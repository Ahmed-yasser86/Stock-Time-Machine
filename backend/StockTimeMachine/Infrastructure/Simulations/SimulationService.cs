using Microsoft.Extensions.Logging;

namespace StockTimeMachine;

public class SimulationService : ISimulationService
{
    private readonly IHistoricalDataRepository _dataRepo;
    private readonly ILogger<SimulationService> _logger;

    public SimulationService(IHistoricalDataRepository dataRepo, ILogger<SimulationService> logger)
    {
        _dataRepo = dataRepo;
        _logger = logger;
    }

    public async Task<Simulation> Run(string symbol, DateOnly entryDate, decimal investmentAmount, DateOnly? exitDate = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new InvalidHistoricalDateException("Symbol is required.");
        if (investmentAmount <= 0)
            throw new InvalidHistoricalDateException("Amount must be greater than zero.");

        var entry = HistoricalDate.Create(entryDate);
        var normalizedSymbol = symbol.Trim().ToUpperInvariant();

        // Fail fast on contradictory input before touching stored data.
        if (exitDate.HasValue)
        {
            var exitCheck = HistoricalDate.Create(exitDate.Value);
            if (exitCheck.Date < entry.Date)
                throw new InvalidHistoricalDateException("Exit date must be on or after the entry date.");
        }

        var entryPrices = await _dataRepo.GetPricesAsOf(normalizedSymbol, entry.Date, 1, ct);
        var entryPrice = entryPrices.FirstOrDefault()?.Close;

        if (entryPrice is null || entryPrice <= 0)
            throw new HistoricalDataNotFoundException($"No price data available for {normalizedSymbol} on {entry.Date}");

        DateOnly effectiveExitDate;
        if (exitDate.HasValue)
        {
            effectiveExitDate = exitDate.Value;
        }
        else
        {
            // US-17: no exit date → most recent available price, labeled via ExitDate.
            var latest = await _dataRepo.GetPricesAsOf(normalizedSymbol, DateOnly.FromDateTime(DateTime.UtcNow), 1, ct);
            var latestPoint = latest.FirstOrDefault();
            if (latestPoint is null)
                throw new HistoricalDataNotFoundException($"No price data available for {normalizedSymbol} on {entry.Date}");
            effectiveExitDate = latestPoint.Date;
        }

        var exitPrices = await _dataRepo.GetPricesAsOf(normalizedSymbol, effectiveExitDate, 1, ct);
        var exitPrice = exitPrices.FirstOrDefault()?.Close;

        if (exitPrice is null || exitPrice <= 0)
            throw new HistoricalDataNotFoundException($"No price data available for {normalizedSymbol} on {effectiveExitDate}");

        var shares = investmentAmount / entryPrice.Value;
        var finalValue = shares * exitPrice.Value;
        var returnPct = Math.Round((finalValue - investmentAmount) / investmentAmount * 100, 2);

        _logger.LogInformation("Simulation for {Symbol}: {Amount} on {Entry} -> {Exit}", normalizedSymbol, investmentAmount, entry.Date, effectiveExitDate);

        return new Simulation
        {
            CompanySymbol = normalizedSymbol,
            EntryDate = entry.Date,
            EntryPrice = entryPrice.Value,
            InvestmentAmount = investmentAmount,
            SharesPurchased = shares,
            ExitDate = effectiveExitDate,
            ExitPrice = exitPrice.Value,
            FinalValue = finalValue,
            ReturnPercentage = returnPct,
        };
    }
}
