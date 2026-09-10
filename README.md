# Every price chart is drawn with knowledge no one had at the time.

Stock Time Machine reconstructs the information environment available up to
a historical cutoff — prices, filings, news, discussion — with proof of what
was knowable and what was not. It then checks whether each significant move's
pre-event information shape has occurred before, across 130 frozen cases,
and reports what followed those precedents descriptively. It detects
patterns and surfaces resemblances. It never predicts, never recommends,
never claims causation.

## What was actually built

- Point-in-time engine: per-item timestamps, Eastern-cutoff plus
  calendar-day bounds, quarantined aftermath — no-hindsight as architecture.
- Deterministic signal library (6 triggers) over frozen hype cases, every
  detection re-derivable from cited evidence.
- Hybrid resemblance (3168-d vectors, dual-query) with same-id, future-peak,
  and duplicate-content exclusions.
- Filing pipeline (SEC EDGAR → structured summaries → briefs), tri-state
  relevance gate, 9,714-company directory.
- Guardrails throughout: honest empties, labeled AI, version-stamped
  methodology (`hcp-v1`, `reg-v1`, `rx-2`).

## Most interesting outputs

- NVDA 2026-06-05: regulatory-overhang on a 20-article chip-blocking thread
  before a −6.20% move ([methodology](docs/methodology.md)).
- 166-article mega-thread resolved into 43 coherent threads by average
  linkage ([analytics](docs/analytics.md)).
- Cosine-1.000 duplicate diagnosed to identical title-only inputs and
  excluded by rule ([data](docs/data.md)).

## Architecture in brief

Domain (pure rules) → Application (orchestration) → Infrastructure
(providers, stores, vectors) → Web (DTOs). Boundaries enforced by tests;
nondeterministic externals sit behind interfaces; 449 tests run fully
offline. Details: [architecture](docs/architecture.md).

## Inspect and run

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project backend/StockTimeMachine.Web/StockTimeMachine.Web.csproj  # :5251
npm --prefix frontend run dev -- --port 5173 --strictPort
dotnet test backend/StockTimeMachine.Tests/StockTimeMachine.Tests.csproj      # 449 green
```

Needs API keys (user-secrets, never committed) and SQL Server; degrades
honestly without them. Full guide: [development](docs/development.md).

## Documentation map

[Overview](docs/overview.md) · [Methodology](docs/methodology.md) ·
[Pipelines](docs/pipelines.md) ·
[Signals](docs/signals.md) · [Analytics](docs/analytics.md) ·
[Architecture](docs/architecture.md) · [Data](docs/data.md) ·
[Providers](docs/providers.md) · [Reproducibility](docs/reproducibility.md) ·
[Testing](docs/testing.md) · [Limitations](docs/limitations.md) ·
[Development](docs/development.md) · [Numbers](docs/data-snapshot.md) ·
[Clustering deep-dive](docs/THREAD_CLUSTERING.md)

**Status: Proof of Concept.** Descriptions are historical only. Known gaps:
title-only GDELT rows with real coverage holes; window-relative regimes;
AI-labeled categories and briefs. See [limitations](docs/limitations.md).
