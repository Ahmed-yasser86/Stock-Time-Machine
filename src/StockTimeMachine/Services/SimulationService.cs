using Microsoft.Extensions.Logging;
using StockTimeMachine.Entities;
using StockTimeMachine.RepositoryContracts;
using StockTimeMachine.ServiceContracts;

namespace StockTimeMachine.Services;

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
        var entryPrices = await _dataRepo.GetPricesAsOf(symbol, entryDate, 1, ct);
        var entryPrice = entryPrices.FirstOrDefault()?.Close;

        if (entryPrice is null || entryPrice <= 0)
            throw new Exceptions.HistoricalDataNotFoundException($"No price data available for {symbol} on {entryDate}");

        var effectiveExitDate = exitDate ?? entryDate;
        var exitPrices = await _dataRepo.GetPricesAsOf(symbol, effectiveExitDate, 1, ct);
        var exitPrice = exitPrices.FirstOrDefault()?.Close;

        if (exitPrice is null || exitPrice <= 0)
            throw new Exceptions.HistoricalDataNotFoundException($"No price data available for {symbol} on {effectiveExitDate}");

        var shares = investmentAmount / entryPrice.Value;
        var finalValue = shares * exitPrice.Value;
        var returnPct = investmentAmount != 0
            ? Math.Round((finalValue - investmentAmount) / investmentAmount * 100, 2)
            : 0m;

        return new Simulation
        {
            CompanySymbol = symbol.ToUpper(),
            EntryDate = entryDate,
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
