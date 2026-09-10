# Data snapshot

Collected 2026-09-09 from the live database, Qdrant Cloud, and the codebase.
Every number below is measured, not estimated. Backend suite: **449/449 green**.

## Registry and corpus

- Hype cases frozen: **130** (MSFT 23, NFLX 23, NVDA 18, AAPL 16, AMZN 15, META 15, TSLA 15, XOM 5)
- Cached news articles: **2,804** (GDELT Cloud 2,780 — all title-only upstream; MarketAux 24 with descriptions)
- Cached article embeddings: **1,083** (model `gemini-embedding-2-preview`)
- Filing summaries (structured, stored): **107**
- Company directory: **9,714** SEC filers (daily SEC mirror + 20-row curated overlay)
- Qdrant: reachable, **925 points** across `hype_threads` (3072-d), `hype_cases_v2` (3168-d), `hype_cases_structural` (96-d)

## Codebase

- Backend: **110 files / ~10,900 lines** (core library; plus Web controllers and 449 tests)
- Frontend: **49 files / ~6,700 lines**
- Layers: Domain / Application / Infrastructure / Web, enforced by architecture tests

## Thresholds and versions (all measured, none guessed)

- Thread clustering: average linkage, **0.75** (chosen by offline A/B over 322 vectors)
- Narrative resemblance: **0.70**; structural/hybrid case vectors: **0.85** (from the 124-case distribution: min 0.547, p25 0.845, median 0.898, p75 0.951, max 0.985)
- Relevance gate: AI confidence floor **0.65** with deterministic RULE fallback; user verdicts final
- Regulatory evidence: 30-day window, tiers at 0–1 / 2–7 / 8–30 days (`reg-v1`)
- Signal catalog: **6** deterministic signals (`hs-v1`); projection `hcp-v1`; prompt `rx-2`

## Worked example (NVDA, peak 2026-06-05, −6.20%)

- Trigger evidence after boundary repair: 20-article Blackwell-blocking thread, 11-article smuggling thread, 2-article Trump-stock thread, 2-article Beijing-probe thread, 4 tense regime days
- Excluded as lone singletons: Bondi health story, Sacks AI-rules, voice-actor lawsuit, 2 others
- Supporters: 12–14 historical cases (future peaks excluded by construction)
- Resemblance: narrative-only matches (0.90 AAPL 2026-06-03); the earlier 0.989 "strong" cross-company echoes were future-dated trigger-echoes, removed by the temporal filter

## Bugs found by measurement (not by review)

- **Similarity 1.00**: identical title + empty description embeds identically under different URLs. Same wire story twice, excluded and counted, never ranked.
- **Chained mega-thread**: 164 articles, median pairwise 0.70, 81% of pairs below the merge bar — three narratives fused by bridge articles. Resolved by average linkage (43 threads, top-cluster medians ~0.79).
- **Hindsight leakage**: June 2 stories attached to a June 1 move (midnight-UTC day rows vs Eastern-evening cutoff). Fixed with the same calendar-day rule filings already used.
- **Company-save truncation**: SEC full venue names vs `nvarchar(20)` voided whole investigations. Normalized at the single write path.
- **Future supporters**: 23 of 35 "historical" supporters peaked after the explained peak. Filtered at the query.

## Known limitations (with examples)

- GDELT Cloud is entity-anchored and title-only: small-cap ETFs (verified: AAAU returns `data: []` upstream with the correct entity) read legitimately empty; keyword fallback was measured and rejected (substring noise: AAACU, ADSU…).
- Entity-anchored corpus starts March 2026; NVDA has zero anchored stories 05-10–05-15 upstream while 05-20+ is populated — moves in the gap honestly show no news.
- Regime labels are window-relative tertiles; "tense" is not comparable across windows (disclosed on every card).
- AI briefs and categories are model-generated and labeled as such; the Bondi case (0.85-confidence REGULATORY on a health story) is why singletons no longer vote alone.
- Full-pipeline investigations take 10–20 minutes (provider pacing + Gemini briefs); GDELT quota (~100 provider-days per sweep) is the binding live constraint.
