
namespace StockTimeMachine;

// Composite persistence port. The focused ports below are the primary
// dependencies: every service depends only on the narrow port(s) it uses
// (pinned by PersistencePorts_AreAdopted). This composite is retained for
// backward compatibility (existing tests, gradual migration) and adds no
// members of its own.
public interface IHistoricalDataRepository : IPriceRepository, IFilingRepository, INewsRepository, IAiCacheRepository
{
}
