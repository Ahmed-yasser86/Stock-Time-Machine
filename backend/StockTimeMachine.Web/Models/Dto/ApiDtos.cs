namespace StockTimeMachine.Web.Models.Dto;

public sealed record CompanyDto(string Symbol, string Name, string Cik, string Exchange, string Sector, string Industry);

public sealed record PricePointDto(DateOnly Date, decimal Open, decimal High, decimal Low, decimal Close, long Volume);

public sealed record FilingDto(string AccessionNumber, string FormType, DateTime FiledAt, DateTime PeriodOfReport, string Url, string Summary);

public sealed record DisclosureDto(string AccessionNumber, string FormType, DateTime FiledAt, string Url, string Title);

public sealed record NewsDto(string Title, string Source, DateTime PublishedAt, string Url);

public sealed record PriceQuoteDto(decimal Open, decimal High, decimal Low, decimal Close, long Volume, DateOnly AsOf);

// Live (delayed) quote for the "What Happened Afterwards" reveal.
// Always post-cutoff context; never part of the historical snapshot.
public sealed record LiveQuoteDto(
    decimal CurrentPrice,
    decimal Change,
    decimal PercentChange,
    decimal High,
    decimal Low,
    decimal PreviousClose,
    DateTime AsOfUtc,
    string Source);

public sealed record OutcomeDto(
    decimal? Price,
    IReadOnlyList<PricePointDto> Prices,
    IReadOnlyList<FilingDto> Filings,
    LiveQuoteDto? LiveQuote);

public sealed record CompanySummaryDto(string Symbol, string Name, string Cik, string Exchange, string Sector);

public sealed record SnapshotResponse(
    CompanySummaryDto Company,
    DateOnly SnapshotDate,
    DateTime CutoffUtc,
    PriceQuoteDto Price,
    IReadOnlyList<PricePointDto> RecentPrices,
    IReadOnlyList<FilingDto> Filings,
    IReadOnlyList<DisclosureDto> CorporateDisclosures,
    IReadOnlyList<NewsDto> News,
    string NewsSource,
    OutcomeDto Outcome,
    IReadOnlyList<string> Warnings);

public sealed record SimulationRequest(string Symbol, DateOnly EntryDate, decimal Amount, DateOnly? ExitDate);

public sealed record SimulationResponse(
    decimal EntryPrice,
    decimal SharesPurchased,
    decimal? ExitPrice,
    decimal FinalValue,
    decimal ReturnPercentage,
    decimal InvestmentAmount,
    DateOnly EntryDate,
    DateOnly? ExitDate,
    string Disclaimer);

public sealed record MethodologyDoc(string Title, string Intro, IReadOnlyList<MethodologySection> Sections);

public sealed record MethodologySection(string Heading, string Body);
