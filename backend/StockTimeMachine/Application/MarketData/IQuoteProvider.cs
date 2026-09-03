
namespace StockTimeMachine;

// A current (delayed) market quote used only for the "What Happened Afterwards"
// reveal and live context. Never part of the historical snapshot.
public sealed record LiveQuote(
    string Symbol,
    decimal CurrentPrice,
    decimal Change,
    decimal PercentChange,
    decimal High,
    decimal Low,
    decimal Open,
    decimal PreviousClose,
    DateTime AsOfUtc,
    string Source);

public interface IQuoteProvider
{
    // Returns null when a live quote is unavailable — callers show an honest
    // empty state instead of failing the investigation. Never throws for
    // provider-side failures.
    Task<LiveQuote?> GetQuoteAsync(string symbol, CancellationToken ct = default);
}
