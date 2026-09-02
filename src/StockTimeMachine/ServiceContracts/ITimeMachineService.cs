using StockTimeMachine.Entities;

namespace StockTimeMachine.ServiceContracts;

public interface ITimeMachineService
{
    Task<HistoricalSnapshot> GetSnapshot(string symbol, DateOnly asOfDate, CancellationToken ct = default);
}
