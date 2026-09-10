# Hype-cycle intelligence: detecting narrative patterns before price events

For researchers. All numbers from [data-snapshot.md](data-snapshot.md);
claims are scoped to the observed dataset, never general truths.

## Research question

Price-based ML dominates market anomaly detection, yet a large share of
what moves markets arrives first as *narrative*: filings, threads, regimes
of coverage that cluster into recognizable shapes days before significant
price moves. Narrative-driven detection is underexplored relative to
price-based ML for a concrete reason — narratives lack ground truth.
There is no labeled dataset of "hype cycles," and any retrospective labeling
is contaminated by hindsight: the labeler already knows what followed.

This system sidesteps the labeling problem instead of solving it. Rather
than training a model to recognize hype, it *freezes* the knowable
information environment at a historical cutoff (130 frozen cases across 8
symbols) and applies deterministic, auditable predicates over hard fields —
thread categories, price flags, regime paths, sentiment direction. The
research question is therefore narrow and falsifiable: **do pre-peak
information shapes recur across companies and time in a way that deterministic
rules can detect, and do their realized aftermaths differ descriptively?**

## The approach

A knowledge-based system, not a black-box model — deliberately. Three
theoretical strands ground the choice:

- **Narrative economics** (Shiller): stories that coordinate attention
  precede coordinated action. The system operationalizes "story" as clustered,
  relevance-gated news threads with measured spans — not vibes.
- **Reflexivity** (Soros): prices and narratives co-evolve, so the system
  never assigns causal direction. Triggers are co-occurrence detectors;
  aftermath panels are descriptive distributions.
- **Sentiment divergence**: when scored coverage leans against the price
  move, the disagreement itself is the signal (`sentiment-split`), measured
  against provider scores plus a local FinBERT fallback — never asserted.

A black-box model would obscure exactly what this project studies: *which*
observable conditions co-occurred. Deterministic predicates keep every
detection inspectable down to the triggering thread.

## Methodology

**Hype-cycle definition.** A hype case = one key move (deterministic
z-score/volume/breakout scoring over 100 trading days) frozen with its
20-day pre-peak narrative threads, regime path, sentiment direction, evidence
counts, and filing summaries. Six signals (`hs-v1`) fire on hard fields only:
earnings-chatter (2+ FINANCIAL threads), regulatory overhang (LEGAL/REGULATORY
thread + 3+ tense days), volume-first divergence (high-volume spike/plunge on
≤2 news items), leadership turbulence (corroborated MANAGEMENT thread),
sentiment split (news against the move), supply-chain tremor (SUPPLY_CHAIN
thread + warming→tense shift).

**Hybrid vector.** Each case embeds as 96 structural dims (fired signals
weighted 3.0, regime transitions 2.0, sentiment 2.0, category mix 1.0, signal
pairs 2.0, 15 co-occurrence pairs, filing dims, magnitude, score, evidence
density) concatenated with 3072 content dims (mean-pooled admitted-thread
embeddings, scaled ×4.0), L2-normalized globally → **3168-d**. Structure
answers "what kind of pattern"; content answers "about what" (fixes
category-only blindness: an earnings miss and an acquisition both read
FINANCIAL structurally but differ in content).

**Why deterministic and auditable.** Every trigger names its evidence
(thread titles, flags, regime dates); every resemblance is labeled
strong/pattern/narrative by which query agreed; every brief cites numbered
claims; every supporter links to its frozen case. A detection can be
re-derived by hand from the registry.

## Creative contributions

- **Qdrant as pattern memory.** Three collections with distinct jobs:
  `hype_threads` (3072-d article recall), `hype_cases_v2` (3168-d hybrid),
  `hype_cases_structural` (96-d pattern-only) — 925 points total.
- **Two-layer retrieval.** Structural query (pattern regardless of topic)
  plus hybrid query (pattern + content), merged strong/pattern/narrative
  with thresholds 0.85/0.85 measured from the case distribution
  (min 0.547, p25 0.845, median 0.898, p75 0.951, max 0.985).
- **Non-causal language contract.** Banned constructions (cause, predict,
  recommend) are enforced in prompts, UI copy, and methodology text — the
  system can only co-locate, resemble, and describe.

## Preliminary findings (observed in this dataset)

- NVDA 2026-06-05 (−6.20%): regulatory-overhang fired on a 20-article
  Blackwell-blocking thread, an 11-article smuggling thread, two 2-article
  threads, plus 4 tense days; 12–14 prior cases showed the trigger, with a
  median realized 5-day move of −0.80% (range −7.95% to +5.53%).
- Thread clustering by average linkage (0.75) resolved a 166-article blob
  into 43 threads (top-cluster medians ~0.79 vs 0.70 pre-fix), verified live.
- Duplicate-content pairs (identical title, empty description) embed
  identically to cosine 1.000; they are excluded and counted, never ranked.
- 50 of 125 registry cases measured carry single-article trigger-category
  threads — the measured exposure surface for thin-evidence votes (registry
  has since grown to 130 cases).

## Limitations

Copied from [data-snapshot.md](data-snapshot.md): GDELT title-only coverage
holes (AAAU verified empty upstream), March-2026 corpus start, window-relative
regimes, AI-labeled categories/briefs (Bondi 0.85 case on record), 10–20
minute investigations, quota-bound live operation. See the snapshot for
examples and numbers.

## Future work

- **Scale:** 130 cases across 8 megacaps cannot support base rates; 1,000+
  cases across sectors would turn aftermath panels from anecdotes into
  distributions and allow threshold calibration with confidence intervals.
- **Social integration:** Arctic Shift coverage is currently a thin layer;
  dense retail-attention series would add the diffusion leg the arrival map
  was designed for.
- **Longitudinal study:** re-running frozen windows as the corpus grows
  would measure detection stability over time — the registry's version
  stamps (`hcp-v1`, `reg-v1`, `rx-2`) exist precisely to make that study
  reproducible.
