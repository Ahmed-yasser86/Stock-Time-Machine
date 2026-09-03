using System.Text.Json;
using StockTimeMachine;

namespace StockTimeMachine.Web.Integrations;

/// <summary>
/// Tertiary STM company-profile lookup backed by Finnhub <c>stock/profile2</c>.
/// Self-contained: typed parsing over a factory-managed <see cref="HttpClient"/>,
/// token read server-side from <c>TradingOptions:FinnhubToken</c> (never logged,
/// never returned). Returns <c>null</c> when the token is missing, the symbol is
/// unknown, or upstream fails — STM never surfaces a hard failure from a fallback.
/// </summary>
public sealed class FinnhubCompanyLookup : ICompanyLookup
{
    private readonly HttpClient _http;
    private readonly ILogger<FinnhubCompanyLookup> _logger;
    private readonly string _token;

    public FinnhubCompanyLookup(HttpClient http, IConfiguration config, ILogger<FinnhubCompanyLookup> logger)
    {
        _http = http;
        _logger = logger;
        _token = config["TradingOptions:FinnhubToken"] ?? "";
    }

    public async Task<Company?> GetCompanyProfileAsync(string symbol, CancellationToken ct = default)
    {
        var normalized = symbol.Trim().ToUpperInvariant();
        if (normalized.Length == 0 || _token.Length == 0) return null;
        try
        {
            using var response = await _http.GetAsync(
                $"https://finnhub.io/api/v1/stock/profile2?symbol={Uri.EscapeDataString(normalized)}&token={_token}",
                ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            static string Get(JsonElement root, string property) =>
                root.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.String
                    ? el.GetString() ?? ""
                    : "";

            var name = Get(root, "name");
            if (name.Length == 0) return null; // unknown symbol yields {}

            return new Company
            {
                Symbol = normalized,
                Name = name,
                Exchange = Get(root, "exchange"),
                Industry = Get(root, "finnhubIndustry"),
                Sector = "", // Finnhub doesn't expose GICS sector
                Cik = Get(root, "cik")
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Finnhub lookup skipped for {Symbol}", normalized);
            return null;
        }
    }
}
