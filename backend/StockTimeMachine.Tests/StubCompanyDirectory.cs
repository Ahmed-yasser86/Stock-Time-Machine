using StockTimeMachine;

namespace StockTimeMachine.Tests;

internal sealed class StubCompanyDirectory : ICompanyDirectory
{
    private readonly Dictionary<string, CompanyInfo> _bySymbol;

    public StubCompanyDirectory(params CompanyInfo[] companies)
    {
        _bySymbol = new Dictionary<string, CompanyInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in companies)
            _bySymbol[c.Symbol] = c;
    }

    public bool TryGet(string symbol, out CompanyInfo? company)
    {
        var hit = _bySymbol.TryGetValue(symbol, out var c) ? c : null;
        company = hit;
        return hit is not null;
    }

    public bool TryGetCik(string symbol, out string cik)
    {
        if (_bySymbol.TryGetValue(symbol, out var c) && !string.IsNullOrEmpty(c.Cik))
        {
            cik = c.Cik;
            return true;
        }
        cik = "";
        return false;
    }

    public IReadOnlyList<CompanyInfo> Search(string query, int maxResults = 10)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<CompanyInfo>();
        return _bySymbol.Values
            .Where(c => c.Symbol.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || c.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Symbol, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .ToList();
    }

    public IReadOnlyList<CompanyInfo> All() => _bySymbol.Values.OrderBy(c => c.Symbol).ToList();
}