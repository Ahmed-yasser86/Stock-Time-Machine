# Limitations

Stated generously: a skeptic should find nothing hidden here.

- **No causation, no prediction.** Nothing in the system establishes that a
  narrative caused a move or that a pattern forecasts anything. Aftermath
  panels are small-sample descriptions from a megacap-skewed registry.
- **Corpus holes are real.** GDELT is title-only with genuine gaps:
  small-cap ETFs can return empty upstream with the correct entity
  resolved; entity-anchored coverage starts unevenly across time; the
  corpus itself starts March 2026. Keyword fallback is rejected by design
  (substring matching returns lookalike noise, not the company).
- **Relevance scores fit, not narrative.** A 1.0 means "about this company
  in this category" — two 1.0 articles can describe different events. The
  documented case: high-confidence REGULATORY on a health story. Lone
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
