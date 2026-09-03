using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace StockTimeMachine;

public class JsonCompanyDirectory : ICompanyDirectory
{
    private readonly Dictionary<string, CompanyInfo> _bySymbol;
    private readonly ILogger<JsonCompanyDirectory> _logger;

    public JsonCompanyDirectory(ILogger<JsonCompanyDirectory> logger)
    {
        _logger = logger;
        _bySymbol = Load();
    }

    private static Dictionary<string, CompanyInfo> Load()
    {
        var path = ResolveDataPath();
        if (!File.Exists(path))
        {
            return new Dictionary<string, CompanyInfo>(StringComparer.OrdinalIgnoreCase);
        }

        var json = File.ReadAllText(path);
        var raw = JsonSerializer.Deserialize<Dictionary<string, CompanyEntry>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new();

        var result = new Dictionary<string, CompanyInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var (symbol, entry) in raw)
        {
            result[symbol.ToUpperInvariant()] = new CompanyInfo(
                symbol.ToUpperInvariant(),
                entry.Name ?? symbol,
                entry.Cik ?? "",
                entry.Exchange ?? "",
                entry.Sector ?? "",
                entry.Industry ?? "");
        }
        return result;
    }

    private static string ResolveDataPath()
    {
        var configured = Environment.GetEnvironmentVariable("STM_DIRECTORY_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        var appRoot = AppContext.BaseDirectory;
        foreach (var rel in new[] { "Domain", "Directory" })
        {
            var c = Path.Combine(appRoot, rel, "companies.json");
            if (File.Exists(c)) return c;
        }
        foreach (var rel in new[] { "Domain", "Directory" })
        {
            var c = Path.Combine(appRoot, "..", "..", "..", "..", rel, "companies.json");
            if (File.Exists(c)) return Path.GetFullPath(c);
        }
        return Path.Combine(appRoot, "Domain", "companies.json");
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
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<CompanyInfo>();

        var q = query.Trim();
        return _bySymbol.Values
            .Where(c => c.Symbol.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || c.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Symbol, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .ToList();
    }

    public IReadOnlyList<CompanyInfo> All() => _bySymbol.Values.OrderBy(c => c.Symbol).ToList();

    private sealed class CompanyEntry
    {
        public string Name { get; set; } = "";
        public string Cik { get; set; } = "";
        public string Exchange { get; set; } = "";
        public string Sector { get; set; } = "";
        public string Industry { get; set; } = "";
    }
}