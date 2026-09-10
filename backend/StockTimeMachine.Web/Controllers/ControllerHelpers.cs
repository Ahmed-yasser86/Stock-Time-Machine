using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using StockTimeMachine.Web.Models.Dto;

namespace StockTimeMachine.Web.Controllers;

// Shared controller plumbing: request validation preludes, SSE framing,
// company mapping, and the operator gate. Every helper preserves the exact
// behavior (messages, status codes, serialization, mapping fallbacks) of the
// inline code it replaces — this file removes duplication, not decisions.
public static class RequestValidation
{
    public static string RequireSymbol(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new InvalidHistoricalDateException("Symbol is required.");
        return symbol.Trim();
    }

    public static DateOnly RequireDate(string? date)
    {
        if (!DateOnly.TryParse(date, out var parsedDate))
            throw new InvalidHistoricalDateException("Date must be a valid yyyy-MM-dd value.");
        return parsedDate;
    }
}

public static class SseWriter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task WriteEventAsync(HttpResponse response, string name, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, Json);
        await response.WriteAsync($"event: {name}\ndata: {json}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    // Stage-progress adapter shared by the live streams: services report
    // sequentially, and the callback blocks briefly to preserve wire order
    // (same semantics as the per-controller adapters this replaces).
    public static IProgress<SnapshotProgress> StageProgress(HttpResponse response, CancellationToken ct) =>
        new Progress<SnapshotProgress>(stage =>
        {
            WriteEventAsync(response, "stage", new
            {
                stage = stage.Stage,
                state = stage.State,
                detail = stage.Detail,
                count = stage.Count
            }, ct).GetAwaiter().GetResult();
        });
}

public static class CompanyMapper
{
    // Directory-fallback chain previously triplicated across controllers:
    // domain entity (when it carries a name) → directory → uppercase stub.
    // The Company? parameter preserves TimeMachineApiController's entity
    // preference; Moves/Hype callers pass nothing and get their exact
    // directory-then-fallback behavior.
    public static CompanySummaryDto Map(ICompanyDirectory directory, string symbol, Company? company = null)
    {
        if (company is not null && !string.IsNullOrEmpty(company.Name))
            return new CompanySummaryDto(company.Symbol, company.Name, company.Cik ?? "", company.Exchange ?? "", company.Sector ?? "");

        if (directory.TryGet(symbol, out var info) && info is not null)
            return new CompanySummaryDto(info.Symbol, info.Name, info.Cik, info.Exchange, info.Sector);

        return new CompanySummaryDto(symbol.ToUpperInvariant(), symbol.ToUpperInvariant(), "", "", "");
    }
}

public static class HypeOperatorGate
{
    // Single home for the operator opt-in check (Hype:HarvestEnabled=true).
    // Disabled by default so product defaults can never change.
    public static bool IsEnabled(IConfiguration config) =>
        string.Equals(config["Hype:HarvestEnabled"], "true", StringComparison.OrdinalIgnoreCase);
}
