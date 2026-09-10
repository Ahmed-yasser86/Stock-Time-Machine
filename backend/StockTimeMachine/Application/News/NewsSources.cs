
namespace StockTimeMachine;

// Canonical news-source keys. The user explicitly selects one per investigation;
// providers are never silently substituted for each other: "gdelt" is always
// the keyless Project DOC API, "gdelt-cloud" is always the authenticated
// Cloud API (which requires Gdelt:ApiKey and fails loudly without it).
public static class NewsSources
{
    public const string Gdelt = "gdelt";
    public const string GdeltCloud = "gdelt-cloud";
    public const string AlphaVantage = "alphavantage";
    public const string MarketAux = "marketaux";

    public static string Normalize(string? source)
    {
        if (string.Equals(source, AlphaVantage, StringComparison.OrdinalIgnoreCase))
            return AlphaVantage;
        if (string.Equals(source, MarketAux, StringComparison.OrdinalIgnoreCase))
            return MarketAux;
        if (string.Equals(source, GdeltCloud, StringComparison.OrdinalIgnoreCase))
            return GdeltCloud;
        return Gdelt;
    }

    public static string DisplayName(string? source) =>
        Normalize(source) == AlphaVantage ? "Alpha Vantage"
        : Normalize(source) == MarketAux ? "MarketAux"
        : Normalize(source) == GdeltCloud ? "GDELT Cloud"
        : "GDELT";
}
