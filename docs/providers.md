# Providers, limits, and failure behavior

## What each source contributes

- **Alpha Vantage** — daily OHLCV bars (the price backbone) and timestamped
  market-aware news with per-entity sentiment. Free tier: 12s pacing,
  25 requests/day, compact windows.
- **SEC EDGAR** — filings and company profiles by CIK. Calendar-date
  eligibility; full exchange names normalized at write time.
- **GDELT Cloud** — entity-anchored stories, title-only, corpus from March
  2026. Day-granularity rows bound by calendar day. Verified holes
  (small-cap ETFs empty upstream) read as honest empties.
- **MarketAux** — entity-tagged recent news with descriptions and
  per-article sentiment; free tier covers recent years only.
- **Arctic Shift** — Reddit discussion (r/wallstreetbets) sliced per move
  ±7 days; best-effort, marked unavailable on failure, never zero-filled.
- **Finnhub** — delayed live quotes (reveal context only) and tertiary
  company-profile fallback.
- **Jina Reader** — article bodies on demand for briefs only, never bulk.
- **Gemini** — embeddings (batch 4) and Flash generations (batch 2),
  contained, cited, labeled AI wherever shown.

## Quota architecture

One global adaptive limiter: per-provider rhythms, 30k tokens/min shared
across Gemini embedding + generation (waits instead of failing), typed
429s with Retry-After backoff (max 5 attempts), one failed fetch per
investigation covering all moves instead of re-hammering. A full sweep
costs on the order of 100 provider-days — quota, not compute, is the
binding live constraint.

## Failure behavior

Provider outage degrades the affected layer to *unavailable* (named, never
zero); triggers requiring missing inputs do not fire; resemblance falls
back Qdrant → in-memory → empty; briefs fail closed to deterministic
thread lists. One failed layer never destroys an investigation.
