using Microsoft.Extensions.Configuration;

namespace StockTimeMachine;

public class NewsProviderFactory : INewsProviderFactory
{
    private readonly GdeltNewsProvider _gdelt;
    private readonly GdeltCloudNewsProvider _gdeltCloud;
    private readonly AlphaVantageNewsProvider _alphaVantage;
    private readonly MarketAuxNewsProvider _marketAux;
    private readonly string _defaultSource;

    public NewsProviderFactory(
        GdeltNewsProvider gdelt,
        GdeltCloudNewsProvider gdeltCloud,
        AlphaVantageNewsProvider alphaVantage,
        MarketAuxNewsProvider marketAux,
        IConfiguration config)
    {
        _gdelt = gdelt;
        _gdeltCloud = gdeltCloud;
        _alphaVantage = alphaVantage;
        _marketAux = marketAux;
        _defaultSource = NewsSources.Normalize(config["News:DefaultSource"]);
    }

    public INewsProvider Get(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return Default();
        var normalized = NewsSources.Normalize(source);
        if (normalized == NewsSources.AlphaVantage)
            return _alphaVantage;
        if (normalized == NewsSources.MarketAux)
            return _marketAux;
        if (normalized == NewsSources.GdeltCloud)
            return RequireCloud();
        return _gdelt;
    }

    public INewsProvider Default()
    {
        if (_defaultSource == NewsSources.AlphaVantage)
            return _alphaVantage;
        if (_defaultSource == NewsSources.MarketAux)
            return _marketAux;
        if (_defaultSource == NewsSources.GdeltCloud)
            return RequireCloud();
        return _gdelt;
    }

    // GDELT Cloud requires a server-side key (Gdelt:ApiKey, Bearer auth).
    // Without it the request fails loudly here — the keyless Project API is
    // a separate explicit source ("gdelt"), never a silent substitute.
    private INewsProvider RequireCloud() =>
        _gdeltCloud.IsConfigured
            ? _gdeltCloud
            : throw new ExternalProviderException(
                "The gdelt-cloud news source requires a configured Gdelt:ApiKey. Select gdelt instead or configure the key.");

    public string DefaultSource => _defaultSource;
}
