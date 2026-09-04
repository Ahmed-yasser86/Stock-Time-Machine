
namespace StockTimeMachine;

// Canonical news-source keys. The user explicitly selects one per investigation;
// providers are never silently substituted for each other.
public static class NewsSources
{
    public const string Gdelt = "gdelt";
    public const string AlphaVantage = "alphavantage";
    public const string MarketAux = "marketaux";

    public static string Normalize(string? source)
    {
        if (string.Equals(source, AlphaVantage, StringComparison.OrdinalIgnoreCase))
            return AlphaVantage;
        if (string.Equals(source, MarketAux, StringComparison.OrdinalIgnoreCase))
            return MarketAux;
        return Gdelt;
    }

    // Transport-neutral: "gdelt" is served by GDELT Cloud (entity-anchored
    // stories) when a server-side key is configured, else the Project DOC API.
    public static string DisplayName(string? source) =>
        Normalize(source) == AlphaVantage ? "Alpha Vantage"
        : Normalize(source) == MarketAux ? "MarketAux"
        : "GDELT";
}
