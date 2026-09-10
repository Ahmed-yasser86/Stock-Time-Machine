# Architecture

Four layers with folder-level separation inside one core assembly, guarded
by architecture tests (`ArchitectureTests`: legacy isolation, no ASP.NET in
core, adapter placement, port adoption), and a full backend suite running
fully offline.

## Layers and responsibilities

- **Domain** (`StockTimeMachine/Domain/`) — entities and pure rules with
  zero infrastructure references: temporal math (`TemporalBoundary`),
  date validation (`HistoricalDate`), material-disclosure rule
  (`SecFiling.IsMaterialDisclosure`), verdict models, exceptions. Entities
  are otherwise anemic by design: this is a read-heavy analytical system,
  so behavioral rules (clustering math, regime/sentiment/uncertainty
  calculators, signal catalog) live as pure statics and services in
  Application, where they are unit-tested without keys or network.
- **Application** (`StockTimeMachine/Application/`) — orchestration across
  explicitly injected narrow ports (`IPriceRepository`,
  `IFilingRepository`, `INewsRepository`, `IAiCacheRepository`,
  `IRangeNewsSearcher`, provider abstractions): projection (freeze),
  evaluation, indexing contracts, methodology content (single source
  served to UI and API). The legacy composite `IHistoricalDataRepository`
  is retained as a compatibility seam; no service depends on it
  (pinned by `PersistencePorts_AreAdopted`).
- **Infrastructure** (`StockTimeMachine/Infrastructure/`) — everything
  external: provider clients (GDELT Cloud and Project, Alpha Vantage,
  MarketAux, Arctic Shift, SEC EDGAR, Finnhub, Jina, Gemini), EF Core
  stores (including `InvestigationJobStore` and the FinBERT sidecar
  client), Qdrant client, job runner.
- **Web** (`StockTimeMachine.Web/`) — controllers that validate input,
  delegate to services, map to DTOs, and frame SSE streams. Shared
  plumbing lives in `Controllers/ControllerHelpers.cs` (validation, SSE
  framing, company mapping, operator gate). Product endpoints
  (`HypeController`) are separate from operator endpoints
  (`HypeOpsController`, harvest-gated); registry-mining math lives in
  `Application/Hype/HypeSupport.cs`, not in endpoints.

## Honest limitations

Separation is folder-level plus tests, not compile-time: Domain,
Application, and Infrastructure compile into one assembly
(`StockTimeMachine.csproj`), so the dependency rule rests on convention
and the architecture tests rather than project references. Namespaces are
flat (`StockTimeMachine`, except `StockTimeMachine.Integrations`), so
layering is not visible in imports either. A three-project split was
deliberately deferred: diff noise outweighs value at this size while the
tests pin the boundaries that matter.

## Why this shape fits this system

The research core (frozen-evidence rules, deterministic triggers, temporal
bounds) must be testable and stable while providers churn, quotas bite, and
models get swapped. The boundary puts every nondeterministic or metered
thing — Gemini wording, provider responses, quota-dependent degradation —
behind interfaces, so the suite pins the science without the network. The
`ICompanyDirectory` / `ICompanyLookup` / provider-factory seams are what let
a hand-maintained list become an SEC-mirror directory with zero consumer
changes.

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
