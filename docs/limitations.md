# Limitations

Stated generously: a skeptic should find nothing hidden here.

- **No causation, no prediction.** Nothing in the system establishes that a
  narrative caused a move or that a pattern forecasts anything. Aftermath
  panels are small-sample descriptions (130 cases, 8 megacaps).
- **Corpus holes are real.** GDELT is title-only with verified gaps: AAAU
  returns `data: []` upstream with the correct entity; NVDA has zero
  anchored stories 05-10–05-15 while 05-20+ is populated; the corpus starts
  March 2026. Keyword fallback was measured and rejected (substring noise).
- **Relevance scores fit, not narrative.** A 1.0 means "about this company
  in this category" — two 1.0 articles can describe different events. The
  documented case: 0.85-confidence REGULATORY on a health story. Lone
  singletons no longer vote alone because of it.
- **Uncertainty is a proxy.** Judgmental weights (0.35/0.35/0.30), not
  estimated risk. Regimes are window-relative tertiles, incomparable across
  windows.
- **Same-week macro shocks inflate structural resemblance** (trigger-echo
  effect); the system labels match kind so echo reads as echo.
- **AI outputs are model-generated.** Briefs, categories, and embeddings
  vary with model versions; verdicts carry version stamps so staleness is
  detectable, not silent.
- **Operational bounds.** 10–20-minute investigations; quota-bound live use
  (~100 provider-days per sweep); no migrations framework; full-suite timing
  flakes on strained machines; exchange codes are display-normalized, not
  preserved verbatim.
