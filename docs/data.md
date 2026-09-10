# Data: domains, flow, provenance

## Domains

- **Companies** — directory (9,714 SEC filers + 20-row curated overlay for
  sectors) for names/CIKs; `Companies` table for resolved profiles. A
  failed save once voided whole investigations; provider strings are now
  normalized and clamped at the single write path.
- **Market prices** — Alpha Vantage daily bars, DB-first, CIK-gated live
  fetch (unknown symbols never burn quota). Raw, unadjusted; splits and
  dividends are not accounted for — stated everywhere prices appear.
- **News/articles** — one row per URL globally (`gdc-`/`av-` hash ids), so
  the same story under two symbols is stored once. GDELT Cloud rows are
  title-only (upstream carries no body); MarketAux rows carry descriptions.
- **Regulatory filings** — SEC EDGAR by CIK, calendar-date eligibility,
  30-day movement windows (`reg-v1`).
- **Sentiment** — provider per-entity scores first, FinBERT sidecar
  (confidence ≥ 0.6) fallback, cached per article.
- **Narratives** — relevance verdicts (AI 0.65 floor → RULE fallback →
  USER final) → embeddings → average-linkage threads (0.75) → optional
  briefs. Every thread exposes its member ids.
- **Hype cases** — 130 frozen dossiers with version stamps (`hcp-v1`,
  `reg-v1`, `rx-2`).
- **Derived measurements** — regimes, divergence, uncertainty, resemblance
  vectors. Computed, never stored as truth.

## Flow

Providers → cache tables → analytical reads (cutoff-filtered, source-pure)
→ per-move evidence → narratives → frozen cases → resemblance/aftermath →
DTOs → UI. Investigation jobs persist payloads first, so disconnects,
restarts, and re-runs reproduce from stored rows rather than re-spending
quota.

## Provenance

Every analytical output traces to source rows: resemblance scores to pooled
article ids; brief claims to numbered citations; filings to accessions/URLs;
threads to member lists; signals to triggering threads, flags, and regime
dates. URLs are stored canonical and rendered verbatim; missing URLs render
title-only, never invented.
