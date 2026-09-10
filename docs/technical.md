# Technical design

For engineers. Every constant below is read from the code it describes;
thresholds link to the measurement that set them.

## System architecture

Clean Architecture with enforced boundaries (architecture tests fail the
build on violations):

- **Domain** — entities, value objects, pure rules. `TemporalBoundary`
  (Eastern-cutoff math), `RegulatoryEvidence` (30-day window + tiers),
  `ArticleRelevance` (tri-state verdicts), `HypeSignalCatalog` (6
  definitions, `hs-v1`). Zero infrastructure dependencies.
- **Application** — orchestration and pure algorithms. `HypeCaseProjection`
  (freeze logic, `hcp-v1`), `HypeSignals` (evaluator), `EmbeddingClustering`
  (average linkage, 0.75), `RegimeClassifier`, `MaterialityRules` (RULE
  fallback), `HypeCaseVector` (3168-d construction), methodology content
  (single source served to UI and API).
- **Infrastructure** — providers (GDELT Cloud/Project, Alpha Vantage,
  MarketAux, Arctic Shift, SEC EDGAR, Finnhub, Jina, Gemini), EF Core QHLYLA,
  Qdrant client, FinBERT sidecar client. Nothing above imports it.
- **Web** — thin controllers mapping to DTOs; no business logic.

Boundaries exist so the deterministic core stays testable without keys,
quota, or network: 449 tests run fully offline against InMemory stores and
stubbed providers.

## The knowledge base (Layer 1)

`HypeCase` row: stable id (`SYMBOL:yyyy-MM-dd`), symbol, peak/decision
dates, source, score, flags, sentiment, completeness, plus `CaseJson`
detail — threads (spans, article ids + dates, categories, basis, briefs),
regime path, evidence (news/filings/social with arrival cascades),
reaction closes, uncertainty, regulatory provenance
(`RegulatoryLookbackDays`, `RegulatoryMethodology`, `RegulatoryTiers`),
projection version + computed-at.

`ArticleEmbeddings` are generated per article per model
(`gemini-embedding-2-preview`, 3072-d), cached by id, reused across
investigations — repeat runs cost zero embedding quota. The non-causal
contract lives in code: banned verbs in `ClusterBriefPrompt`,
`HypeBriefPrompt`, and methodology strings; briefs carry per-claim
citations or fail closed.

## The signal library (Layer 2)

Six deterministic signals with exact triggers (see `HypeSignals.cs`):
earnings-chatter (2+ FINANCIAL threads), regulatory-overhang (LEGAL/
REGULATORY thread + 3+ tense days, multi-article threads vote),
volume-first divergence (high-volume spike/plunge, ≤2 news, evidence present),
leadership-turbulence (corroborated MANAGEMENT thread), sentiment-split
(news against the move), supply-tremor (SUPPLY_CHAIN + warming→tense).

Hybrid vector per case: dims 0–5 fired signals (×3.0), 6–8 magnitude/score/
density, 10–11 regime transitions (×2.0), 12–15 sentiment (×2.0), 16–49
category share+volume (×1.0), 50–64 signal pairs (×2.0), 65–80 regime bigrams,
81–88 sentiment-mean + filings, 89–95 filing dims; then 3072 content dims
(×4.0). Weighted first, L2-normalized globally — so cosine compares
emphasis, not raw magnitudes. Weighted cosine over raw cosine because an
unweighted 3072-d content mean would drown the 96 structural dims that
carry the pattern identity.

Qdrant collections: `hype_threads` (article vectors for narrative joins),
`hype_cases_v2` (full 3168-d hybrid), `hype_cases_structural` (96-d
structure-only). Writes happen at registry time and harvest, never on the
read path; one-shot reindex backfilled 124 cases into 591 points.

## Live detection (Layer 3)

`GET hype/signals` → moves detection → per-move evidence → narratives →
projection → trigger evaluation → supporters (same trigger, peakDate ≤
explained peak) → resemblance → aftermath. `signals/stream` mirrors it over
SSE with stage events.

Merge logic: structural-only hits → `pattern`; hybrid-only → `narrative`;
both agree → `strong`, ranked first. Same-article pairs excluded (they join
at 1.0 by construction); same-symbol peaks within 30 days excluded (shared
window overlap); future peaks excluded (hindsight); distinct-id pairs at
≥0.999 excluded as duplicates and counted. Fallback chain per layer:
Qdrant → in-memory join → empty, each degrading loudly in logs, never
failing the response.

## Data integrity decisions

- **Materiality filter before LLM briefs:** `HypeBriefInputFilter` drops
  noise threads (e.g., unrelated SpaceX coverage) pre-token; briefs receive
  grounded inputs plus exact stage facts, never raw library dumps.
- **Completeness per case:** full/partial/missing per area; triggers
  requiring missing inputs do not fire (absence never reads as pattern).
- **Non-predictive disclaimer enforcement:** aftermath panels render
  collapsed with disclaimers; DTOs carry no forecast fields, so the UI
  cannot render one by accident.

## Rate limiting and concurrency

One global adaptive limiter (`AdaptiveRateLimiter` + registry): per-provider
rhythms (Alpha Vantage 12s pace, GDELT 3s batches of 2), Gemini 30k TPM
shared across embeddings + generation (waits instead of failing), Jina 2s
batches of 4. Provider 429s propagate as typed exceptions with Retry-After
backoff (up to 3 retries); one failed fetch per investigation covers all
moves instead of re-hammering. Investigation jobs persist before the
pipeline starts (disconnect-safe), 60-minute timeout, 7-day prune.

## Known technical limitations

- **Sparse case saturation:** 130 cases cannot calibrate thresholds tightly;
  the 0.85/0.85 cutoffs sit just above p25 of the measured distribution by
  judgment, not by statistics.
- **What vectors cannot capture:** filing text beyond summaries, non-English
  nuance (clustered separately by design), same-week macro shocks (which
  inflate structural similarity — the documented trigger-echo effect).
- **Singletons and thin evidence:** 12–13% of threads match nothing; 40% of
  registry cases carry single-article trigger threads. The system labels
  both instead of hiding them.
