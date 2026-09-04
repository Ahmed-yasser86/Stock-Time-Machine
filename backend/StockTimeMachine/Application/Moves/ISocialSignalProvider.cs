namespace StockTimeMachine;

// Retail-discussion surface. Implementations return items whose publication
// instant falls inside [from, to] (inclusive, UTC); undated items are dropped,
// never coerced. Provider faults degrade to empty — never throw for them.
public interface ISocialSignalProvider
{
    string ProviderName { get; }
    Task<IReadOnlyList<SocialSignal>> GetSignals(
        string symbol, string? companyName, DateOnly from, DateOnly to, CancellationToken ct = default);
}
