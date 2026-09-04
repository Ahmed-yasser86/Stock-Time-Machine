namespace StockTimeMachine;

// Public-attention surface. No implementation is registered yet: the official
// Google Trends API is in gated alpha (application-only) and the unofficial
// pytrends path was rejected (archived repo, ToS-gray, rate-fragile).
// Register an implementation only for an official, credentialed source, and
// always present Index as the relative 0-100 measure it is.
public interface ISearchInterestProvider
{
    string ProviderName { get; }
    Task<IReadOnlyList<InterestPoint>> GetInterest(
        string keyword, DateOnly from, DateOnly to, CancellationToken ct = default);
}
