# Stock Time Machine API

Base URL (dev): `http://localhost:5251`. All errors are RFC 7807 `ProblemDetails`
with user-facing copy and a `traceId`. Money is `decimal`; absent values are `null`,
never `0` sentinels.

## `GET /health` / `GET /`

Liveness and service info. No database access.

## `GET /health/db`

Readiness: process is up AND the cache database answers (200 reachable,
503 otherwise). Compose healthcheck and K8s readinessProbe target this,
not `/health`.

## `GET /api/timemachine/company-search?q=`

Single canonical company search: directory first, then persisted companies, deduped
by symbol (max 10). Empty query returns `[]`.

Response: `[{ symbol, name, cik, exchange, sector, industry }]`

## `GET /api/timemachine/snapshot?symbol=&date=&newsSource=&sections=`

Full historical investigation. `date` is `yyyy-MM-dd` (past dates only; today and
future are rejected with `400`). `newsSource` is `gdelt` (default),
`gdelt-cloud`, `alphavantage`, or `marketaux`; exactly one source is used
per investigation — never mixed, never substituted. `gdelt` is the keyless
GDELT Project archive. `gdelt-cloud` is the separate authenticated Cloud
API (entity-anchored stories, resolved by company name with
ticker-identifier verification; corpus starts March 2026, so older windows
return honest empty states) and requires a server-side `Gdelt:ApiKey` —
without it the source fails loudly with `503` instead of substituting.
`marketaux` is entity-tagged finance news with per-article timestamps
(free tier 100 req/day; recent-years coverage — January 2025 verified,
January 2020 empty).

`sections` optionally rescopes to a comma-separated subset of
`prices,filings,news,outcome` (unknown keys are a `400`). Unrequested sections return
empty without touching the database or any provider — this powers per-section retry
and lets clients skip slow sources instead of waiting out their timeouts.
Omitted or empty means all sections. Warnings describe requested sections only.

Response: company, `snapshotDate`, `cutoffUtc` (23:59:59 US/Eastern as UTC), historical
`price` + `recentPrices` (raw OHLCV, Alpha Vantage), `filings` (10-K/10-Q), 
corporate disclosures (8-K),
News (selected source only), outcome (post-cutoff prices + filings + delayed live quote), warnings.
`news` carries ONLY relevance-admitted articles (same gated corpus as narrative
threads and move evidence) plus a `newsRelevance` census (`considered`,
`relevant`, `irrelevant`, `uncertain`) over the evaluated candidates.

## GET /api/timemachine/snapshot/stream (SSE)

Same parameters as snapshot. Streams one honest stage event per pipeline step (started/complete/failed/skipped with counts), then the full snapshot as the final event. Validation errors are normal 400s before streaming starts; mid-stream failures arrive as an error event.

## GET /api/timemachine/moves?symbol=&date=&newsSource=

100-trading-day investigation window: deterministic Key Moves (score = 0.5 return surprise + 0.3 volume anomaly + 0.2 range break; top 5; 30-day minimum), each with evidence already filtered to that move's own cutoff (filings, news with per-article `id`, social signals, 5-day market reaction), plus window summary stats (cumulative return, annualized volatility, max drawdown, best/worst days), decision uncertainty index (0–100 with per-term breakdown), per-day market regimes (`calm|normal|tense|warming`), per-move sentiment divergence, arrival cascades, and the analyzed price slice for the timeline view. Social layer: Arctic Shift Reddit archive (keyless, 7-day lookback); unavailable layers are named, never padded. News is DB-first with a staleness guard: when the newest cached row predates the latest move by 7+ days, one live refresh runs per investigation.

## GET /api/timemachine/moves/stream?symbol=&date=&newsSource=

Live investigation stream: `stage` events (`detecting` → per-move `evidence` → `embedding` counts → `clustering` → per-thread `briefing`, each started/complete with details) then the full `moves` and `narratives` payloads as named events. Same data as the two GETs, narrated while it computes. Mid-stream failures arrive as an `error` event.

## GET /api/timemachine/narratives?symbol=&date=&newsSource=

Window narrative threads over cached news only (zero provider quota). Threads cluster ONLY relevance-admitted articles: the shared gate (AI verdicts, deterministic RULE fallback when AI is off, explicit user approvals) runs before embeddings, so embeddings answer "which admitted articles relate", never "is this relevant". Gemini embeddings decide membership (fallback: TF-IDF), shared terms name each thread; multi-article threads (largest 8) carry opt-in-model AI briefs (`summary`, `keyPoints`, `model`). Response states `clusteringMethod` (`gemini-embeddings` | `tf-idf-fallback`), `articlesConsidered`, and the gate census (`relevantCount`, `irrelevantCount`, `uncertainCount`); borderline articles wait on `narratives/candidates` for explicit approve/reject. Briefs are non-deterministic and hindsight-exposed — labeled as generated everywhere shown.

## GET /api/timemachine/compare/brief?symbols=&date=&newsSource=&terms=

Opt-in shared-story brief across exactly 2 picks' cached coverage: articles matching ≥2 shared terms (max 8) briefed as one story with per-article citations. Never a joint verdict — cross-company causation and pooled conclusions are banned in the prompt. Null brief when nothing matches or AI is off.

## GET /api/timemachine/compare/threads?symbols=&date=&newsSource=

Cross-pick thread pairs ranked by embedding cosine (per-symbol clusters joined by max-pairwise similarity, threshold 0.70, top 10). Each pair carries both titles and its score so users judge every join; empty when AI is off. Scores are similarity, never relatedness proofs.

## POST /api/timemachine/copilot/suggest

Phrases caller-supplied deterministic gap pointers as next steps (max 5). The model phrases only — routes and links stay frontend-owned and must be preserved verbatim. Shares the copilot containment contract and the 30k-tpm budget.

## POST /api/timemachine/copilot/{filings-summary|contrast|explain-uncertainty|gist|review}

Evidence copilot: explicit AI actions over already-retrieved evidence, never auto-run. Body: `{ symbol, date, newsSource?, ids?, note? }`. `filings-summary` briefs the move's filings; `contrast` needs ≥2 article `ids` and reports agreement first; `explain-uncertainty` translates the measured uncertainty components into plain words (no new numbers); `gist` renders an English gist of non-English threads; `review` checks the user's conclusion note claim-by-claim and returns `{ ref, verdict: supported|unsupported|unclear, detail }[]` — it reviews, never authors. Null briefs / empty issues on disabled AI or empty evidence.

## GET /api/timemachine/hype/signals?symbol=&date=&newsSource=

Hype-cycle signal detections per key move: deterministic triggers (flags, thread categories, regime path, sentiment) with triggering-evidence refs, supporting registry cases (same trigger fired), cache-only embedding resemblance (0.70 threshold, scored), and realized post-peak closes. Registry joins are cross-source by design; every supporter/resemblance/followed ref carries its own `newsSource` provenance badge, and deep-links reopen the case under its own source — never the viewer's. Movement-level regulatory evidence uses the 30-day candidate window (methodology `reg-v1`, tiers very_close/recent/older); window length and version ride on every case. Co-occurrence only — predicts nothing.

## POST /api/timemachine/hype/brief

Opt-in analyst summary for one detected signal (`{ symbol, date, newsSource?, peakDate, signalId }`). Grounded in the triggering threads under the thread-brief containment contract; the regulatory section lists only in-window filings and every filing claim cites its primary document (SEC accession when stored, else the directory URL, else stated untraced). Null when AI is off or the model declines.

## POST /api/timemachine/hype/harvest

Offline case harvesting (`{ symbol, date, newsSource?, topMoves? }`). Operator opt-in only (`Hype:HarvestEnabled`, default off → 404); the harvest script is the sole caller. Product top-5 default is never affected.

## POST /api/timemachine/hype/backfill-regulatory

One-shot regulatory methodology migration (`reg-v1`), same operator gate as harvest. Recomputes movement-level regulatory evidence under the 30-day window for every frozen HypeCase and every non-running investigation job from stored payloads only (no provider calls). Idempotent — clean rows are detected and skipped; running jobs are never touched. Returns `{ hypeCasesUpdated, hypeCasesSkipped, jobsUpdated, jobsSkipped }`.

## GET /api/timemachine/quote?symbol=

Delayed live quote (Finnhub, 60s cache). Honest 404 when no token is configured.

## POST /api/timemachine/simulation

Deterministic raw-price hypothetical (decimal math). No exit date defaults to the most recent available price, labeled via exitDate. Permanent raw-price disclaimer in every response.

## GET /api/timemachine/methodology

Source/boundary/limitation disclosures served to the UI, including the Key Moves scoring weights.

## Rate limits & throttling (all providers)

One global adaptive limiter (AIMD per named scope: 429s halve the batch and
double spacing/backoff, success streaks recover gradually, pending work waits
instead of dropping). Production rhythm in `RateLimits` config. Throttling
never changes response shapes — sections go empty/unavailable with their
usual honest states.

- **Alpha Vantage:** 12s pacing, batch 1; 25 req/day free tier. Daily-limit
  429s are not retried; `outputsize=full` premium-denial retries once as
  `compact`, then serves.
- **MarketAux:** 100 req/day free tier; paced; daily-limit 429s are not retried.
- **GDELT Cloud + Project:** `Retry-After` honored (capped 60s), else adaptive
  backoff, max 5 attempts, then honest degradation. One failed fetch per
  investigation covers all moves instead of re-hammering.
- **Gemini:** per-endpoint limiters (`gemini-embed` + `gemini-generate`, each
  with token budget + adaptive rhythm); true batching on embeds; throttled
  items requeue under a slower rhythm. Embedding vectors persist per article
  and model, so repeat investigations reuse them at zero cost.
- **Arctic Shift:** paced, deliberately never retried (durable per-IP
  throttle); single spaced fetch per investigation.
- **Jina Reader:** paced; fail-soft per call with stored text fallback.
- **Model-specific quotas:** Gemini embed vs generate limits differ, so each
  gets its own limiter instance and token budget over the shared mechanism
  (`RateLimits:gemini-embed` vs `RateLimits:gemini-generate`).
