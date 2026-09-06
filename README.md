# Stock Time Machine

A pre-decision research instrument: reconstruct what investors could have known at a
specific moment in a stock's history — then reveal what actually followed.

> "Before I act on this thesis today — let me see what investors actually knew at the
> last comparable moment. Not what we know now. What they knew *then*."

This is a research instrument, **not investment advice**. Simulations use raw prices;
splits and dividends are not factored in.

## Product in one paragraph

Pick a company and a historical date. The snapshot engine rebuilds the information
environment that existed on or before that date (temporal cutoff: 23:59:59 US/Eastern,
converted to UTC) from real providers — Alpha Vantage (prices), SEC EDGAR (filings),
and your selected news source (Alpha Vantage or GDELT). A separate "What Happened
Afterwards" reveal shows subsequent prices, post-cutoff filings, and a delayed live
quote. An optional hypothetical calculator quantifies a past-tense "what if" with a
permanent raw-price disclaimer.

## Repository layout

| Path | Role |
|---|---|
| `backend/StockTimeMachine` | Core library: `Domain/`, `Application/`, `Infrastructure/` (Onion-style layers) |
| `backend/StockTimeMachine.Tests` | xUnit suite: domain, services, providers, API contracts |
| `backend/StockTimeMachine.Web` | API-first ASP.NET Core backend (`/api/timemachine/*`), no server-rendered MVC |
| `frontend/` | React + Vite + Tailwind v4 + shadcn/ui enterprise investigation workspace |
| `backend/legacy/` | Legacy Stocks-trading and Contacts modules — quarantined, unreferenced by the product (see below) |

## Architecture

```
HistoricalSnapshot (Domain)
    ↑
TimeMachineService (Application orchestration, no HttpContext)
    ↓
ICompanyRepository / IHistoricalDataRepository / providers (Contracts)
    ↓
EF Core + AlphaVantage / SEC EDGAR / GDELT / Finnhub (Infrastructure)
    ↓
Thin API controllers → ProblemDetails errors → React dossier UI
```

Key rules enforced by tests on every run:

- **Temporal integrity** — `TemporalBoundary.GetCutoffUtc(DateOnly)` is the single
  cutoff. SEC filings use calendar-day eligibility (`StartOfDayAfterUtc`); news uses
  true timestamps; daily bars use trade dates. A next-day filing can never leak into
  an earlier snapshot (tested).
- **Money is `decimal`** — never `double`/`float`.
- **DB before network** — every investigation checks persisted rows before any
  external call (Alpha Vantage free tier: 25 req/day).
- **One provider failure never kills an investigation** — sections degrade to honest
  empty/partial states (`FailedSections`).
- **Secrets stay server-side** — keys are read from configuration only, never logged,
  never returned by APIs, never sent to browsers.

## API

| Endpoint | Purpose |
|---|---|
| `GET /health`, `GET /` | Liveness / service info |
| `GET /api/timemachine/company-search?q=` | Directory + persisted companies, deduped |
| `GET /api/timemachine/snapshot?symbol=&date=&newsSource=` | Full historical snapshot (`newsSource`: `gdelt` default, or `alphavantage`) |
| `GET /api/timemachine/quote?symbol=` | Delayed live quote (Finnhub, cached 60s) |
| `POST /api/timemachine/simulation` | Deterministic raw-price hypothetical |
| `GET /api/timemachine/methodology` | Source/boundary/limitation disclosures served to the UI |

Errors follow RFC 7807 `ProblemDetails` with user-story copy (single middleware
pipeline, Serilog logging, `traceId` on every error).

## External integrations & configuration

| Provider | Use | Limits honored |
|---|---|---|
| Alpha Vantage | Historical OHLCV + optional news source | 25 req/day, 5 req/min — DB-first, compact/full output sizing, rate-limit → 429 |
| SEC EDGAR | Filings + company identity | Contact `User-Agent` on every request (config `SecEdgar:UserAgent`), 10 req/s etiquette |
| GDELT | Default news source: Cloud (entity-anchored stories) when `Gdelt:ApiKey` is configured server-side, else the keyless Project archive (7-day window, best-effort) | Cached in DB, 25s client timeout, timeouts degrade to empty; Cloud corpus starts March 2026 |
| MarketAux | Optional third news source: entity-tagged finance news with sentiment + timestamps | Key via user-secrets only; 100 req/day, ~3 articles/req; recent-years coverage (Jan 2025 ✓, Jan 2020 empty); cached in DB |
| Finnhub | Company-profile fallback + delayed live quotes for the reveal | Existing token config untouched (`TradingOptions:FinnhubToken`), quotes cached 60s (60 calls/min tier) |

Configure via `appsettings.Development.json` or environment variables
(`AlphaVantage__ApiKey`, `Gdelt__ApiKey`, `TradingOptions__FinnhubToken`).
`appsettings.json` ships with empty placeholders documenting the surface.

## Local development

Prerequisites: .NET 9 SDK, SQL Server (Windows auth, `StockTimeMachineDb`), Node 20+.

```powershell
# Backend (http://localhost:5251)
dotnet run --project backend/StockTimeMachine.Web/StockTimeMachine.Web.csproj --launch-profile http

# Frontend (http://localhost:5173, or preview dist on :4173)
cd frontend
npm install
npm run dev            # VITE_API_URL=http://localhost:5251 assumed by default
```

```powershell
dotnet test backend/StockTimeMachine.Tests/StockTimeMachine.Tests.csproj
cd frontend; npm run build; npm run lint
```

## Deliberate architectural decisions

- **Contacts module: kept isolated, zero coupling.** It has no product relationship to
  investigations and is not referenced by the web backend. A future watchlist must be
  a new STM-bounded context, not a reuse of `Person`/`Country`.
- **Legacy Stocks paper-trading: preserved, not extended.** Its useful Finnhub logic
  was consolidated behind `IFinnhubService` (+ `ContainsKey` quote guard fix) and
  adapted via `FinnhubCompanyLookup` / `FinnhubQuoteProvider`.
- **Browser-direct Finnhub WebSocket retired on purpose.** The old Trade view passed
  the API token to browser JavaScript — incompatible with the no-secret-leakage
  invariant. Live context is served via a cached server-side quote endpoint instead.
- **Duplicate pipelines merged.** One company search, one error pipeline (middleware),
  one canonical Finnhub client, one `TemporalBoundary`.
- **News sources are never mixed.** `NewsSources` + `INewsProviderFactory` resolve
  exactly the user-selected source; failures yield source-labeled empty states.

## Testing

- `backend/StockTimeMachine.Tests` (106 tests): temporal-boundary DST/leap coverage,
  next-day filing exclusion, provider-failure isolation, news-source non-mixing,
  decimal/determinism simulation proofs, offline provider tests (mock HTTP), API
  contract tests via `WebApplicationFactory`.
- Legacy `StocksUnitTest` has 3 pre-existing order-CRUD failures (unconfigured
  mocks) unrelated to STM; verified identical with and without current changes.
