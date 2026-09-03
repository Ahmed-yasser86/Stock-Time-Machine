using Microsoft.Extensions.Configuration;

namespace StockTimeMachine;

public class NewsProviderFactory : INewsProviderFactory
{
    private readonly GdeltNewsProvider _gdelt;
    private readonly GdeltCloudNewsProvider _gdeltCloud;
    private readonly AlphaVantageNewsProvider _alphaVantage;
    private readonly string _defaultSource;

    public NewsProviderFactory(
        GdeltNewsProvider gdelt,
        GdeltCloudNewsProvider gdeltCloud,
        AlphaVantageNewsProvider alphaVantage,
        IConfiguration config)
    {
        _gdelt = gdelt;
        _gdeltCloud = gdeltCloud;
        _alphaVantage = alphaVantage;
        _defaultSource = NewsSources.Normalize(config["News:DefaultSource"]);
    }

    public INewsProvider Get(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return Default();
        return NewsSources.Normalize(source) == NewsSources.AlphaVantage
            ? _alphaVantage
            : Gdelt();
    }

    public INewsProvider Default() =>
        _defaultSource == NewsSources.AlphaVantage ? _alphaVantage : Gdelt();

    // "gdelt" is one source, two transports: authenticated Cloud (entity-anchored
    // stories) when a server-side key is configured, otherwise the keyless
    // Project DOC API. Never mixed within an investigation.
    private INewsProvider Gdelt() => _gdeltCloud.IsConfigured ? _gdeltCloud : _gdelt;

    public string DefaultSource => _defaultSource;
}
