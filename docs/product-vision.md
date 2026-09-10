# Product Vision & Extensions

Thesis: the product reconstructs **historical information environments** —
what information was available about a company at a specific point in
time, what narratives existed, how they evolved, and what could
reasonably have been known then. It is not a stock dashboard and not a
collection of AI features; prices are one layer among several, and every
output carries its provenance, cutoff, and uncertainty. The system never
predicts, never recommends, and never claims causation — those are
permanent constraints, not missing features (see [methodology](methodology.md)).

This document separates what exists today from genuine extensions. Anything
below that is already implemented is stated as baseline, not vision.

## Baseline: already implemented (not vision)

- **Point-in-time reconstruction.** Every section (prices, filings, news,
  discussion) is cutoff-filtered per item; post-cutoff material lives only
  in quarantined aftermath panels. Temporal integrity is test-pinned
  ([testing](testing.md)).
- **Narrative clustering.** Relevance-admitted articles embed once, merge by
  average linkage, and carry labels, categories, and member lists; threads
  are inspectable to canonical URLs ([thread-clustering](thread-clustering.md)).
- **Two-company comparison.** Shared-story briefs and cross-thread pairs
  with cohesion and membership on both sides.
- **Evidence grounding.** Briefs cite triggering threads; verdicts carry
  decision/source/category; methodology answers cite sections and refuse
  out-of-scope questions.
- **Arrival data.** Per-move, per-layer first-seen stamps with lags
  (prices, filings, news, social).
- **Multi-source cache.** Four news transports, SEC filings, Arctic Shift
  Reddit archive, live quotes — all behind ports, all fail-soft.

## 1. Historical intelligence: established core

Reconstructing the knowable-then vs known-later distinction *is* the
current product, not a future direction. The genuine extension is depth,
not concept: richer per-item provenance (retrieval timestamps, corpus
version at fetch time) and reconstruction over longer horizons where
provider coverage thins. See [limitations](limitations.md) for current
corpus bounds.

## 2. Narrative intelligence: from threads to lifecycles

Today the system identifies threads within one investigation window. It
does not track a narrative across windows: emergence, splits and merges,
competition between rival framings, persistence, and disappearance. The
extension is **narrative lifecycle analysis** — resolving thread identity
over successive as-of dates from stored rows only (no re-fetching, same
frozen-evidence discipline), so a researcher can ask how a story evolved
rather than what a snapshot contained.

## 3. Comparative intelligence: beyond performance

Today comparison covers two companies' threads and briefs. The extension
is comparing **how companies experienced the same information
environment differently**: relevance composition, narrative structure,
evidence timing, attention (see §6), and market context side by side —
still descriptive (no ranking, no pooled verdicts, per the existing
compare-contract constraints).

## 4. Evidence and provenance: formal grading

Today outputs cite sources and label AI generation. The extension is a
formal, machine-readable evidence taxonomy on every claim: directly
observed (cached row) vs reconstructed relationship (join/projection) vs
model-generated interpretation vs quantified uncertainty — so consumers
of the API (and future researchers, §8) can filter by evidence grade
instead of reading prose disclaimers.

## 5. Multi-source information ecosystem

Today: reporting (news), regulatory (SEC), discussion (Reddit archive),
prices, quotes. Reddit coverage is real but narrow (one archive, keyed
subreddits). Genuine additions, each as a port behind the existing
provider seams: earnings-call and interview **transcripts**, **analyst
commentary**, company communications, **blogs**, **podcasts**, public
video media. This is not "more sources" for coverage's sake: reporting
captures what was published, while discussion and commentary reveal how
information was interpreted, contested, amplified, or circulated — the
vision is reconstructing the environment **across channels**, with per-
channel provenance preserved (never pooled verdicts).

## 6. Information diffusion and attention

Today arrival maps record per-layer first-seen order and lags — the raw
material of diffusion, but not its analysis. Two genuine extensions:

- **Diffusion sequences**: event → reporting → analyst discussion →
  social amplification → competing narrative → attention shift, studied
  as temporal sequence and co-occurrence. Causal language stays banned;
  the system distinguishes sequence, co-occurrence, diffusion, and
  attention as separate, labeled claims.
- **Attention measurement**: an `ISearchInterestProvider` port already
  exists with no registered implementation (deliberately — only an
  official, credentialed source qualifies). Filling it would add the
  attention axis the arrival data currently lacks.

## 7. Decision reconstruction (not advice)

Today the copilot reviews a user's note against cited evidence and
suggests next steps. The extension moves from "what information existed?"
to **"what decisions were reasonable given the information available
then?"** — reconstructing informational constraints and available
evidence around historical decisions. Framed strictly as
decision-*environment* reconstruction: no recommendations, no
counterfactual profit claims, same containment contract as current
copilot actions.

## 8. Research platform

Today there is no dataset export and no research-facing access beyond
the product API. The direction is making frozen cases, threads,
verdicts, and arrival data available as versioned research corpora with
stable schemas — infrastructure for computational work on narratives,
diffusion, attention, and historical decision-making, where every row
already carries the timestamps and version stamps such work requires.

## 9. AI as a research interface (not a chatbot)

Today the copilot performs bounded actions over already-retrieved
evidence. The extension is evidence-constrained interrogation of frozen
cases: "what were the dominant narratives around this company on this
date?", "what information distinguished company A from company B?",
"show me the pre-date evidence supporting this narrative" — answered
only from stored rows, with citations, temporal bounds, and
uncertainty attached. The AI never gets a broader context window than
the evidence the question is allowed to see.

## 10. Beyond markets: horizon only, not roadmap

The framework (cutoff-filtered corpus → frozen cases → deterministic
predicates → resemblance → quarantined aftermath) is domain-agnostic in
principle and could one day describe technology developments, policy,
geopolitical or economic events, and public narratives. Today the
implementation is coupled to market data at every layer (symbols,
prices, filings, moves), so this is a horizon to protect architecturally
(ports, frozen schemas, no market logic in generic machinery) — not a
committed direction and not a claim about non-market capability.

## Constraints that travel with every extension

- Non-predictive, non-advisory, non-causal — methodology, not modesty.
- No silent substitution: sources, transports, and evidence grades stay
  distinct end to end.
- Frozen evidence discipline: new capabilities read stored rows; nothing
  re-fetches or re-decides history.
- Degradation before failure: absent inputs produce honest empty states
  with reasons, never invented data.
