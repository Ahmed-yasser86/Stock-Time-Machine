# Every price chart is drawn with knowledge no one had at the time.

Stock Time Machine reconstructs the information environment available up to
a historical cutoff — then checks whether each significant move's pre-event
information shape occurred before, and reports what followed, descriptively.
It detects patterns and surfaces resemblances. It never predicts, never
recommends, never claims causation.

## Built

- **Point-in-time engine** — per-item timestamps, cutoff + calendar-day
  bounds, quarantined aftermath. No-hindsight as architecture.
- **Signal library** — 6 deterministic triggers over frozen cases, every
  detection re-derivable from cited evidence.
- **Resemblance** — 3168-d hybrid vectors, dual-query, with same-id,
  future-peak, and duplicate-content exclusions.
- **Pipelines** — SEC filings → summaries → briefs; tri-state relevance
  gate; SEC-mirror directory; honest empties; labeled AI; version-stamped
  methods (`hcp-v1`, `reg-v1`, `rx-2`).

## Worth a look

- Corroborated threads vote; lone singletons stay visible but don't
- Chained mega-threads split into single narratives by average linkage
- Identical inputs embed identically — duplicates counted, never ranked

## Structure

Domain → Application → Infrastructure → Web. Boundaries enforced by tests;
externals behind interfaces; suite runs fully offline.
[Architecture](docs/architecture.md) · [Pipelines](docs/pipelines.md)

## Run

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project backend/StockTimeMachine.Web/StockTimeMachine.Web.csproj  # :5251
npm --prefix frontend run dev -- --port 5173 --strictPort
dotnet test backend/StockTimeMachine.Tests/StockTimeMachine.Tests.csproj
```

Keys via user-secrets, SQL Server local; degrades honestly without either.
[Development](docs/development.md) · [Overview](docs/overview.md) ·
[Methodology](docs/methodology.md) · [Signals](docs/signals.md) ·
[Analytics](docs/analytics.md) · [Data](docs/data.md) ·
[Providers](docs/providers.md) · [Reproducibility](docs/reproducibility.md) ·
[Testing](docs/testing.md) · [Limitations](docs/limitations.md)

**Status: Proof of Concept.** Historical descriptions only. Known gaps:
title-only rows with real coverage holes; window-relative regimes; AI-labeled
categories and briefs.
