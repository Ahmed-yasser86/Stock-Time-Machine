# Signal library (`hs-v1`)

Six deterministic triggers over frozen hype cases. Each fires only on hard
fields — thread categories, price flags, regime path, sentiment direction —
and names its evidence. A trigger whose required inputs are missing does not
fire: absence of data never reads as a pattern. Lone single-article threads
never vote alone (a mention is not a condition); they stay visible in
drill-down with their category basis.

## earnings-chatter

- **Problem:** detect coordinated earnings conversation before a peak.
- **Inputs:** qualified pre-peak threads with `TopCategory == FINANCIAL`.
- **Method:** fires when ≥2 such threads exist.
- **Output:** one evidence line per thread (title + category basis) plus
  thread ids for resemblance joins.
- **Interpretation:** financial topics are clustering pre-peak.
- **Limitation:** FINANCIAL is a broad bucket (an earnings miss and an
  acquisition share it); thread drill-down, not the label, carries the meaning.

## regulatory-overhang

- **Problem:** detect a regulatory/legal cloud forming before a peak.
- **Inputs:** qualified LEGAL/REGULATORY threads; regime path.
- **Method:** fires when ≥1 **multi-article** thread exists **and** ≥3
  pre-peak days read tense. Single-article threads cast no vote.
- **Output:** per-thread lines (title, size, relevance, basis, rationale)
  plus the tense-day span.
- **Interpretation:** regulatory conditions co-occurred with the peak window.
- **Limitation:** categories are AI- or keyword-assigned and can misfire —
  the documented case is a 0.85-confidence REGULATORY verdict on a health
  story about an AI-advisory appointee. Corroboration (multiple articles)
  is what keeps such cases out of triggers; the thread remains inspectable.

## volume-first-divergence

- **Problem:** detect price movement on thin narrative.
- **Inputs:** move flags + evidence news count, evidence layer present.
- **Method:** fires on HighVolume with Spike/Plunge when evidence holds ≤2
  news items. Requires the evidence layer (missing layer ≠ thin narrative).
- **Output:** flag list + news count lines.
- **Interpretation:** the market moved while coverage was sparse.
- **Limitation:** cannot distinguish "no narrative" from "source has no
  coverage" — a coverage gap reads the same as silence.

## leadership-turbulence

- **Problem:** detect management instability chatter.
- **Inputs:** qualified MANAGEMENT threads.
- **Method:** fires on ≥2 threads, or 1 thread plus a second feature (tense
  day, contrarian sentiment, or high-volume flag).
- **Output:** per-thread evidence lines.
- **Interpretation:** management is being discussed from multiple angles.
- **Limitation:** one passing "CEO" mention is filtered by the corroboration
  bar, but a thin two-thread case can still be weak — check sizes.

## sentiment-split

- **Problem:** surface disagreement between coverage and price.
- **Inputs:** mean scored-news sentiment vs move direction.
- **Method:** fires when signs oppose (|mean| ≥ 0.1; <2 scored articles →
  unknown, single scores are noise, never consensus). Scores come from
  providers with per-entity sentiment, else confidence-gated local FinBERT.
- **Output:** one contrarian-divergence line.
- **Interpretation:** a contrarian lens for investigation.
- **Limitation:** needs ≥2 scored articles; silent providers + down sidecar
  = unknown, not neutral.

## supply-tremor

- **Problem:** detect supply-chain stress coinciding with volatility shift.
- **Inputs:** SUPPLY_CHAIN threads + ordered regime path.
- **Method:** fires on ≥2 threads (or 1 + second feature) **and** a
  warming→tense shift (a Warming index with a later Tense).
- **Output:** thread lines + shift line.
- **Interpretation:** supply stress co-occurred with a volatility regime shift.
- **Limitation:** regime labels are window-relative; the "shift" is
  descriptive of realized volatility, not a structural break test.
