# Reproducibility: what reproduces, and what cannot

Four distinct claims, kept separate:

1. **Deterministic algorithms.** Greedy agglomerative merging, tertile
   computation, trigger predicates, cutoff math — same inputs, same outputs,
   no randomness anywhere. Verified: there are no random seeds in the
   backend because there is nothing to seed — no Louvain, no permutation
   testing, no sampling. Do not document seeds that do not exist.
2. **Versioned inputs and methodology.** Frozen cases stamp `hcp-v1`
   (projection), `reg-v1` (regulatory window), `rx-2` (relevance prompt);
   embeddings record model + revision (FinBERT rev `4556d130`); briefs
   record their model. A result is reproducible *for a stated version set*.
3. **Reproducible analytical logic.** 449 tests pin trigger math, boundary
   rules, counting rules, and merge behavior offline (InMemory stores,
   stubbed providers) — including byte-identical clustering reruns.
4. **Nondeterministic external dependencies.** Gemini wording and categories,
   provider responses, quota-dependent degradation paths, and cache growth
   all change outputs legitimately. A recomputation months later with new
   cache rows is *new input*, not nondeterminism — the frozen registry row
   is the reproducible artifact, labeled when live results diverge from it.

Schema has no migrations framework (`EnsureCreated` plus manual SQL);
backfill endpoints ship with table backups first. Re-running history is
therefore procedural and logged, never silent.
