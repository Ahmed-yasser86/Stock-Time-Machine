# Testing strategy: invariants, not counts

The backend suite (green) is organized around research and system
invariants. Representative coverage by area:

- **Temporal integrity** — day-boundary guards (June-2 leak regression),
  cutoff enforcement, post-peak member quarantine, future-peak exclusion
  from supporters and resemblance.
- **Analytical math** — trigger predicates per signal, corroboration bars,
  uncertainty components, regime tertiles, average-linkage chaining fixture
  (two 0.94 pairs + 0.82 bridge splits; fuses under single linkage).
- **Missing data** — absent layers read missing/empty/unavailable (never
  zero); triggers gated on completeness; honest-empty UI states.
- **Comparison logic** — duplicate-content exclusion with counts,
  representative-independent matching, cutoff enforcement, member/URL
  preservation.
- **Provider failure** — 429/fallback paths (GDELT resilience suite), FinBERT
  down → provider scores, Qdrant unreachable → in-memory → empty.
- **Rate limiting** — pacing, batching, backoff, per-investigation fetch caps.
- **Determinism** — byte-identical clustering reruns; version-stamp
  comparisons; threshold pins requiring re-measurement to change.
- **Domain behavior** — company resolution, exchange normalization (the
  `nvarchar(20)` incident), backfill idempotency.

Known caveat, documented not hidden: InMemory stores ignore column widths,
so the Exchange truncation class is pinned at the code-contract level
(mapped codes + clamps), not at the SQL level. Timing-sensitive provider
tests can flake under parallel load on strained machines; they pass in
isolation and are retried, not weakened.
