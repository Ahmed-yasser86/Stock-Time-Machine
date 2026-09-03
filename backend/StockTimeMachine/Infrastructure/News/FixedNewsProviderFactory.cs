
namespace StockTimeMachine;

// Single-provider factory for tests and simple host setups.
public sealed class FixedNewsProviderFactory : INewsProviderFactory
{
    private readonly INewsProvider _provider;

    public FixedNewsProviderFactory(INewsProvider provider, string? defaultSource = null)
    {
        _provider = provider;
        DefaultSource = NewsSources.Normalize(defaultSource);
    }

    public INewsProvider Get(string? source) => _provider;

    public string DefaultSource { get; }
}
