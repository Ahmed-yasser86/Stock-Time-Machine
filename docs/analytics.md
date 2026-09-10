# Analytics

One section per distinctive feature, same shape: Problem, Inputs, Method,
Output, Interpretation, Limitation. Constants match the implementation
verbatim.

## Decision uncertainty (`dc-v1`)

- **Problem:** calibrate confidence before acting on a window.
- **Inputs:** per-layer temporal completeness (filings, news, social,
  prices), FinBERT scores ≥ 0.6 confidence, window volatility + drawdown.
- **Method:** coverage = 1 − mean completeness (0.35); conflict =
  confidence-weighted score variance over ≥3 usable articles (0.35);
  instability = 0.6·(vol/50) + 0.4·(|drawdown|/25) (0.30); renormalized over
  measured components only, plus a Confidence grade. Zero-window case reads
  maximally uncertain, never vacuously certain.
- **Output:** 0–100 score with per-component values and plain-language detail
  strings.
- **Interpretation:** higher = thinner, more conflicting, or more unstable
  evidence. A transparent proxy for information constraint.
- **Limitation:** this is a descriptive proxy composite, **not modeled
  risk**. The weights (0.35/0.35/0.30) are judgmental, not estimated; treat
  the grade as seriously as the number.

## Information arrival

- **Problem:** see what an investor could have seen *first*.
- **Inputs:** per-move evidence with source timestamps.
- **Method:** per-layer first-appearance instant; lags measured against the
  earliest observed layer.
- **Output:** arrival cascade (regulatory → news → social → market) with
  first-seen instants and lag hours.
- **Interpretation:** ordering in time is observation, not causation.
- **Limitation:** day-granularity layers resolve to calendar days; intraday
  ordering within GDELT/filings is not measurable.

## Sentiment divergence

- **Problem:** spot disagreement between narrative and market.
- **Inputs:** per-article scores (provider per-entity first, FinBERT ≥ 0.6
  fallback), move return.
- **Method:** sign(mean) vs sign(return); |mean| < 0.1 → neutral; <2 scored
  articles → unknown.
- **Output:** agree / disagree / neutral / unknown per key move.
- **Interpretation:** disagreement is a contrarian lens for investigation.
- **Limitation:** requires scored coverage; assumes scores measure
  company-relevant sentiment, which holds only where providers score
  per-entity or FinBERT is correctly calibrated.

## Market regimes

- **Problem:** read volatility context at a glance.
- **Inputs:** trailing closes per analyzed day.
- **Method:** trailing annualized volatility, tertiled **within the window**
  (bottom calm, middle normal, top tense); <10 priors → warming.
- **Output:** per-day labels shading the timeline.
- **Interpretation:** realized-volatility description of this window.
- **Limitation:** window-relative by construction — "tense" here is not
  comparable to "tense" in another window. Every cross-window surface
  repeats this warning.

## Cross-company comparison

- **Problem:** compare two names without mixing their timelines or verdicts.
- **Inputs:** both picks' cached prices + articles under one shared cutoff.
- **Method:** common trading days only (dropped days counted, never
  interpolated); each series indexed to 100 on the first shared day;
  thread pairs ranked by embedding cosine with cohesion/mean/shared-terms
  reported separately; vocabulary overlap kept as its own weaker layer.
- **Output:** aligned chart, window stats per pick, pair cards with member
  drill-down, duplicate-content exclusions counted.
- **Interpretation:** co-occurrence and resemblance for researcher judgment.
- **Limitation:** no pooled verdicts by design; shared article ≠ shared
  narrative ≠ causal relationship.

## Sector sweep

- **Problem:** survey up to 8 symbols under one cutoff without cross-symbol
  contamination.
- **Inputs:** symbol list (cap 8), shared date, one source.
- **Method:** the full single-symbol pipeline per symbol, sequentially;
  per-symbol failures become error rows; unknown symbols get explanatory
  rows instead of silent zeros.
- **Output:** one row per symbol with peaks, signals, completeness.
- **Interpretation:** a compact multi-name diligence surface.
- **Limitation:** cost scales per symbol (~100 provider-days each); no
  ranking or pooled conclusions anywhere.

## Aftermath

- **Problem:** show what followed without letting it leak backward.
- **Inputs:** recorded closes after each peak, frozen at harvest.
- **Method:** first→last realized moves aggregated (median/high/low);
  collapsed behind click with disclaimers; DTOs carry no forecast fields.
- **Output:** per-case reaction closes + aggregate panel.
- **Interpretation:** description of what happened after similar shapes —
  base rates to think with, not predictions.
- **Limitation:** small, megacap-skewed sample;
  distributions, not estimates.
