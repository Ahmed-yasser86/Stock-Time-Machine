
namespace StockTimeMachine;

public interface ISimulationService
{
    Task<Simulation> Run(string symbol, DateOnly entryDate, decimal investmentAmount, DateOnly? exitDate = null, CancellationToken ct = default);
}
