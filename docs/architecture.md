# Architecture

Four layers with enforced dependency direction (boundary violations fail the
build) and 449 tests running fully offline.

## Layers and responsibilities

- **Domain** — entities and pure rules, zero infrastructure references:
  temporal math, regulatory windows, verdict models, signal catalog,
  clustering math, regime/sentiment/uncertainty calculators. The research
  logic lives here, where it can be unit-tested without keys or network.
- **Application** — orchestration across explicitly injected interfaces:
  projection (freeze), evaluation, indexing contracts, methodology content
  (single source served to UI and API). No HTTP, no SQL, no file access.
- **Infrastructure** — everything external: provider clients (GDELT Cloud
  and Project, Alpha Vantage, MarketAux, Arctic Shift, SEC EDGAR, Finnhub,
  Jina, Gemini), EF Core stores, Qdrant client, FinBERT client, job runner.
- **Web** — thin controllers mapping domain results to DTOs. No business
  logic; no analytical math.

## Why this shape fits this system

The research core (frozen-evidence rules, deterministic triggers, temporal
bounds) must be testable and stable while providers churn, quotas bite, and
models get swapped. The boundary puts every nondeterministic or metered
thing — Gemini wording, provider responses, quota-dependent degradation —
behind interfaces, so the suite pins the science without the network. The
`ICompanyDirectory` / `ICompanyLookup` / provider-factory seams are what let
a 20-row hand list become a 9,714-row SEC mirror with zero consumer changes.

## Hybrid vector layout (3168-d)

Dims 0–5 fired signals (×3.0); 6–8 magnitude/score/density; 10–11 regime
transitions (×2.0); 12–15 sentiment (×2.0); 16–49 category share+volume
(×1.0); 50–64 signal pairs (×2.0); 65–80 regime bigrams; 81–88
sentiment-mean + filings; 89–95 filing dims; then 3072 content dims (×4.0).
Weighted first, L2-normalized globally, so cosine compares emphasis rather
than raw magnitudes — an unweighted content mean would drown the 96
structural dims that carry pattern identity.

## Request flow (single investigation)

`investigate` → company resolution (directory → SEC profile → Finnhub
fallback → transient) → prices (DB-first, CIK-gated live fetch) → filings
→ news per source (cache, then one live fetch per empty window) → moves
detection → per-move evidence (gated, windowed, arrival-stamped) → narratives
(embed → cluster → brief) → hype projection → triggers → resemblance →
aftermath. Long runs persist as jobs first (disconnect-safe, 60-minute
timeout, 7-day prune) and stream stages over SSE.

## What the architecture does not guarantee

Isolation is not determinism: identical code with different provider
responses, model versions, or quota states produces different runs. The
architecture makes that boundary explicit and testable — it does not remove
the externals. There is no migrations framework (`EnsureCreated` plus manual
SQL with backups); schema changes are procedural, not automatic.
