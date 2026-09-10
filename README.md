# Every price chart is drawn with knowledge no one had at the time.

Stock Time Machine is a point-in-time reconstruction engine with a hype-cycle
pattern library: given a company and a date, it rebuilds the knowable
information environment — then checks whether the pre-event information
shape has occurred before. It detects patterns and surfaces resemblances.
It never predicts.

- → Researchers: see [docs/research.md](docs/research.md)
- → Engineers: see [docs/technical.md](docs/technical.md)
- → Business: see [docs/overview.md](docs/overview.md)

## Try it (against a warmed instance)

- **NVDA · 2026-06-05** — regulatory-overhang fired on a 20-article
  chip-blocking thread plus 4 tense days before a −6.20% move
- **NVDA · 2026-06-15** — full-window sweep: 5 peaks, sentiment-split and
  regulatory-overhang each with a dozen-plus verified prior cases
  (future-dated cases excluded by construction)
- **NVDA vs AAPL · 2026-06-09** — cross-company thread comparison with
  cohesion, shared terms, and duplicate-content handling

## Stack

- Backend: .NET 9, Clean Architecture (Domain / Application /
  Infrastructure / Web), EF Core + SQL Server
- Intelligence: Gemini embeddings + Flash (contained, cited, labeled),
  local FinBERT sidecar, SEC EDGAR, GDELT, Alpha Vantage, MarketAux
- Vectors: Qdrant Cloud (3168-d hybrid, 96-d structural, 3072-d threads)
- Frontend: React + TypeScript, Vite, SSE streaming
- Tests: 449 backend tests green, offline, quota-free

## Limitations (honest, enforced in product)

- Descriptions are historical only — realized aftermath, never forecasts.
- GDELT coverage is title-only with genuine holes (small-cap ETFs verified
  empty upstream); regimes are window-relative; AI outputs are labeled.
- Full investigations take 10–20 minutes under provider pacing and quotas.

**Status: Proof of Concept.** Numbers: [docs/data-snapshot.md](docs/data-snapshot.md).
