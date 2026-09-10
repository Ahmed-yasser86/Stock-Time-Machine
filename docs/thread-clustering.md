# Thread Clustering & Evidence Traceability

> Technical evidence record for the average-linkage decision. The
> user-facing summary lives in the methodology API ("Thread Clustering"
> section, served from `MethodologyContent.cs`).

## 1. What we did (chronology)

1. **Evaluation (read-only).** A 166-member NVIDIA thread (`nvidia · chip · china`,
   NVDA as-of 2026-06-23) was suspected of fusing distinct narratives. Of the
   166 member ids, 164 had titles retrievable from cache; audit of those 164
   found zero off-topic articles but three fused sub-narratives:
   China/export controls (~42), GPU/hardware (~40), general AI (~72).
2. **Root cause identified.** `EmbeddingClustering.cs` used greedy agglomerative
   clustering with **single linkage** (max pairwise cosine) at 0.75. One bridge
   article above the bar fuses otherwise weakly related subgroups.
3. **Controlled A/B (offline, cached vectors only, no production change).**
   Strategies: single @ 0.75 (baseline), average @ 0.70/0.75/0.80, single @ 0.75
   + coherence post-pass, single @ 0.80. Two cases: NVDA 06-23 (172 vectors) and
   NVDA 06-15 (150 vectors) — 322 vectors total.
4. **Winner implemented.** Average linkage @ 0.75, same threshold, one-function
   diff (`ClusterSimilarity`: max → mean). Shipped in `9237116`.
5. **Traceability built.** `GET narratives/articles` resolves exact thread member
   ids against cached clustering-input rows; thread titles expand inline to
   "Articles in this thread (N)" with canonical URLs. Shipped in `96bbdda`.
6. **Methodology slimmed.** The in-product "Thread Clustering" section follows
   the existing plain-language pattern; this document holds the full evidence.

## 2. Achievements

- The 166-article mega-thread now resolves live into **43 coherent threads**
  (top sizes 38/20/16/14/11/8/6/5), matching the offline A/B measurement for
  that case (43 clusters, largest 38 — §4, Average @ 0.75 row).
- Internal coherence of large clusters: median pairwise similarity **~0.70 →
  ~0.79**, sub-threshold pairs **~80% → ~10–20%**.
- Singleton rate held at **12–13%** (valid singleton output — articles with
  no above-threshold partner — not clustering errors).
- Every thread is now inspectable down to canonical article URLs; live audit
  returned exact membership (38/38, 20/20, 16/16 on NVDA; 9/9 on MSFT) with
  no observed membership discrepancies against the cached reference rows
  (no independent human ground truth exists for these threads) and zero
  invented links.
- Full suite **429/429** (at time of writing), tsc clean, vite build clean,
  secret scans clean.

## 3. Architecture — pipeline data path

```
providers (GDELT Cloud / AV / MarketAux)
  → NewsArticles cache (title, URL, publishedAt, source — canonical rows)
  → relevance gate (ArticleRelevances verdicts: decision/source/category)
  → Gemini embeddings → ArticleEmbeddings cache (3072-d, per model)
  → EmbeddingClustering.Cluster (average linkage, 0.75, deterministic)
  → TopicCluster { LabelTerms, ArticleIds, ArticleDates, TopCategory, ... }
  → HypeCaseProjection.ToThread → HypeCaseThread (frozen, incl. CategoryBasis)
  → UI topics payload (ArticleIds included) ─┬─ renders thread list
                                             └─ drill-down: GET narratives/articles
                                                → SAME GetNewsAsOf cache read
                                                → exact-id filter → stored verdicts
```

Key invariants:

- **One source of truth for article metadata:** the `NewsArticles` cache rows.
  The inspection endpoint reads the same rows clustering consumed; it never
  re-clusters, never similarity-searches, never invents.
- **Determinism boundary:** clustering is deterministic given vectors
  (byte-identical reruns verified); vectors themselves are model-generated
  (hence the AI label). Fresh cache rows or new embeddings legitimately
  change later recomputations — that is new input, not nondeterminism.
- **No silent re-clustering on inspection:** expanding a thread performs zero
  clustering work; counts shown (`N of M requested`) expose cache shortfalls.

## 4. A/B evidence (summary)

Measured on 172 vectors (case 1) / 150 vectors (case 2):

| Strategy | Clusters | Largest | Blob median | Pairs < 0.75 | Singletons | Separates narratives? |
|---|---|---|---|---|---|---|
| Single @ 0.75 (old) | 9 / 10 | 164 / 140 | 0.70 / 0.71 | 81% / 79% | 8 / 8 | No |
| Average @ 0.70 | 15 / 15 | 99 / 78 | 0.72 / 0.73 | 68% / 66% | 7 / 7 | No — still blobs |
| **Average @ 0.75** | **43 / 39** | **38 / 37** | **~0.79** | **~10–20%** | **22 / 18 (12–13%)** | **Yes, both cases** |
| Average @ 0.80 | 77 / 69 | 24 / 24 | ~0.82 | ~0–4% | 51 / 48 (30%+) | Yes, but fragmented |
| Single + post-pass | = avg .75 | = avg .75 | = avg .75 | = avg .75 | = avg .75 | Yes (bit-identical — redundant) |
| Single @ 0.80 | 45 / 41 | 114 / 97 | 0.73 | 66% / 63% | 35 / 32 | No — blob persists, members shed |

"Separates narratives?" = whether the three fused sub-narratives from §1
(China/export-controls vs GPU/hardware vs general AI) end up in distinct
clusters rather than one blob. "No — still blobs" means cluster counts
rose but the dominant cluster still spans multiple sub-narratives;
"No — blob persists, members shed" means the blob survived while smaller
clusters peeled off as singletons.

Additional measurements:

- **Bridges:** 64/164 blob members (39%) averaged < 0.70 similarity to the
  rest — quantified chaining, not a hunch.
- **Merge log:** 165 merges descending 1.000 → ~0.75; strongest rejected
  pairs at 0.747/0.739/0.732 — the old boundary was razor-thin and arbitrary.
- **Runtime:** similarity matrix ~8s per 174 articles in throwaway Python;
  clustering proper ~1s. This measures experiment cost only, not a
  language comparison; at these volumes (embedding ceiling: 400 articles)
  there is no perf concern either way.
- **Determinism:** harness rerun byte-identical across processes.

## 5. Decision record

- **Linkage:** average (mean pairwise cosine). Rejected single (chains),
  rejected post-pass (zero benefit over average, extra machinery).
- **Threshold:** 0.75 unchanged. 0.70 under-splits, 0.80 fragments;
  retuning single linkage is proven futile. Changing the constant later
  requires re-measurement (pinned by
  `EmbeddingClustering_ThresholdIs075`).
- **Scope:** global. One merge rule for every investigation; per-case
  switching would be arbitrary tuning.
- **Goal (binding):** one thread = one semantically coherent narrative.
  Not "more clusters", not "fewer articles per cluster". Singleton growth
  8→22 is accepted honest output, never optimized away.

## 6. Downstream effects

Observed (verified live):

- **Signal voting:** triggers now vote on single-narrative threads. The old
  blob cast one STRATEGY vote for three stories; isolated threads vote their
  own categories (verified: smuggling thread votes LEGAL on its own).

Expected consequences of the new behavior (not separately measured):

- **Resemblance:** mean-pooling over tight threads should sharpen case
  vectors (previously one 166-vector soup per case).
- **Briefs:** the top-8 briefing budget covers 8 focused threads instead of
  1 blob + scraps; token increase bounded by the existing caps.
- **Evidence weighting:** thread size now reflects narrative support rather
  than chaining accidents.

## 7. Verification & tests

- `EmbeddingClustering_BridgePairDoesNotChainClusters` — hand-computed 2D
  fixture (two 0.94 pairs, one 0.82 bridge, 0.56 cross-mean): fuses under
  single linkage, splits under average. Fails on the old code by construction.
- `EmbeddingClustering_ThresholdIs075` — pins the evaluated operating point.
- `ThreadArticlesTests` (13 tests) — exact membership, order, verdict
  metadata, missing/duplicate/empty ids, URL preservation, endpoint mapping
  + validation (ids required, 500 cap), input-output identity regression.
- Live: NVDA 06-23 threads 38/38 + 20/20 + 16/16 exact; MSFT 05-18 threads
  9/9; all URLs canonical, all verdicts RELEVANT, narratives match A/B
  predictions. One defensible boundary article (Warren-seeks-CEO-testimony:
  Huang-adjacent and China-adjacent) — honest boundary behavior, not a bug.
- DOM click-through was attempted 3× via headless browser; each run expired
  waiting on the unrelated AI-briefs pipeline stage. The component binds
  type-checked to the verified DTOs and mirrors the proven ThreadGist
  interaction pattern.

## 8. Commits

- `9237116` — average-linkage implementation + chaining fixture.
- `96bbdda` — inspection endpoint + drill-down UI + audit docs.
- Follow-up: methodology slim-down (this change).

## 9. Operations

- GDELT quota is the binding live constraint: each full investigation
  traverses ~100 provider days. Verification batteries should be budgeted.
- Known honest limits: GDELT entity-anchored corpus under-covers small-cap
  ETFs (verified: AAAU returns `data: []` upstream with correct entity);
  keyword fallback was measured and rejected (substring noise).
