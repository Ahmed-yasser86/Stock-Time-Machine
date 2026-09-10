
namespace StockTimeMachine;

// Composite persistence port kept for backward compatibility during the ISP
// migration. New consumers should depend on the focused interfaces directly:
// IPriceRepository, IFilingRepository, INewsRepository, IAiCacheRepository.
public interface IHistoricalDataRepository : IPriceRepository, IFilingRepository, INewsRepository, IAiCacheRepository
{
}
