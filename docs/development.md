# Development: run, configure, verify

## Prerequisites

- .NET 9 SDK, Node 20+, SQL Server (local), optional: Python + torch for
  the FinBERT sidecar (`scripts/nlp/`). A strained machine will flake
  timing-sensitive provider tests — rerun those in isolation.

## Secrets (never committed)

`dotnet user-secrets --project backend/StockTimeMachine.Web set <key> <value>`
for: `AlphaVantage:ApiKey`, `Gdelt:ApiKey`, `MarketAux:ApiKey`,
`Gemini:ApiKey`, `Jina:ApiKey`, `Qdrant:ApiKey` + `Qdrant:Host`,
`TradingOptions:FinnhubToken`. The app runs degraded without them (honest
empty states), never broken.

## Run

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project backend/StockTimeMachine.Web/StockTimeMachine.Web.csproj  # :5251
npm --prefix frontend run dev -- --port 5173 --strictPort
python scripts/nlp/server.py --port 5252   # optional sentiment sidecar
```

Never overlap `dotnet run` with `dotnet test` (the server locks build
outputs — stop it first). Frontend builds needing memory:
`$env:NODE_OPTIONS = "--max-old-space-size=4096"`.

## Gates

```powershell
dotnet test backend/StockTimeMachine.Tests/StockTimeMachine.Tests.csproj  # suite green
npx --prefix frontend tsc --noEmit
npm --prefix frontend run build
.\scripts\verify.ps1   # end-to-end gate where available
```

## Data operations

- Company directory refresh: `scripts/refresh-company-directory.ps1`
  (SEC mirror + curated overlay).
- Regulatory backfill: back up the table first
  (`SELECT * INTO HypeCases_backup_YYYYMMDD FROM HypeCases`), then
  `POST /api/timemachine/hype/backfill-regulatory` with the harvest gate
  enabled; re-run until everything reports skipped (idempotent).
- Harvesting (operator only): `scripts/hype-harvest.ps1` with
  `Hype:HarvestEnabled=true`.
