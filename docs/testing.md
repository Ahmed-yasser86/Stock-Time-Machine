# Testing strategy: invariants, not counts

461 tests, 38 classes, green on every push (`dotnet test
backend/StockTimeMachine.Tests/StockTimeMachine.Tests.csproj`; CI runs the
same command plus frontend lint + build). The suite is hermetic: xUnit +
Moq + EF InMemory + `WebApplicationFactory`, stubbed Gemini/providers,
zero network, zero quota spend. A test that needs the network is a bug in
the test — nondeterministic externals sit behind interfaces precisely so
the suite can pin the science without them.

## Coverage by area (classes, counts, what they pin)

**Temporal integrity** — `TemporalBoundaryTests` (22: DST transitions,
leap days, filing in/out of cutoff, determinism), `ArrivalMapTests` (6:
layer ordering with lags, silent layer reads unknown-not-zero,
regulatory layer window-scoped), `RegulatoryEvidenceTests` (13: 30-day
window ending on move date, custom lookbacks), `RegulatoryBackfillTests`
(5: `MigrateCaseDetail_IsIdempotent`).

**Analytical math** — `HypeTests` (74: trigger predicates per signal,
`CaseVector_LayoutWeightsAndNormalization`, resemblance exclusions),
`DecisionContextTests` (11: `Renormalization_ExactMath`),
`RegimeClassifierTests` (4: `Classify_IsDeterministic`),
`SentimentDivergenceTests` (6), `TopicClusteringTests` (7:
`Cluster_IsDeterministic`, average-linkage chaining fixture — two 0.94
pairs + 0.82 bridge splits, fuses under single linkage),
`AiNarrativeTests` (46: `EmbeddingClustering_ThresholdIs075`,
400-article embedding ceiling, TF-IDF fallback paths),
`MoveDetectionServiceTests` (18: `GetMoves_IsDeterministic`, arrival-map
attachment), `HypeSupportTests` (5: supporter exclusion rules,
aftermath median/high/low math).

**Missing data** — absent layers read missing/empty/unavailable, never
zero (`ArrivalMapTests.Build_SilentLayer_IsUnknownNotZero`);
triggers gated on completeness; honest-empty UI states
(`HypeSectorTests`: unknown-symbol and thin-history rows carry reasons,
9 tests).

**Comparison logic** — `HypeSectorTests` (9: shared cutoff, per-symbol
failure isolation, 8-symbol cap), duplicate-content exclusion with
counts, representative-independent matching, member/URL preservation
(`ThreadArticlesTests`, 13).

**Provider failure** — `GdeltResilienceTests` (5: 429/backoff paths),
`ProviderFixtureTests` (22: timeouts degrade to empty, `IsConfigured`
gates), `GdeltCoverageTests` (12), `AlphaVantageNewsProviderTests` (5:
`DeterministicIds_AreStable`), `GeminiClientTests` (6), FinBERT down →
provider scores, Qdrant unreachable → in-memory → empty.

**Rate limiting** — `RateLimiterTests` (11: pacing, batching, backoff,
per-investigation fetch caps).

**Persistence** — `HistoricalDataRepositoryTests` (14),
`CompanyRepositoryTests` (13: `Add_NormalizesAndClampsProviderStringsToColumns`,
exchange-variant mapping — NYSE/NASDAQ/OTC codes plus clamps, the
`nvarchar(20)` incident class), `JsonCompanyDirectoryTests` (10).

**Services & pipeline** — `TimeMachineServiceTests` (12),
`SimulationServiceTests` (12), `SnapshotEngineIntegrationTests` (4),
`InvestigationBehaviorTests` (19: source normalization, factory
selection incl. the loud `gdelt-cloud`-without-key failure),
`InvestigationJobTests` (6: terminal CAS transitions, stale reaping,
timeout termination, concurrent jobs), `RelevanceGateTests` (31: verdict
precedence USER > AI > RULE, store-failure resilience),
`Copilot`/brief paths inside `AiNarrativeTests`.

**API contracts** — `ApiContractTests` (17 via `WebApplicationFactory`:
`/health`, `/health/db` reachability, company-search, snapshot warnings,
methodology copy pins like the 0.5/0.3/0.2 score formula).

**Architecture** — `ArchitectureTests` (8): legacy isolation, no
ASP.NET in core, Finnhub adapter placement, range-search abstraction,
persistence-port adoption (constructors take focused ports, never the
composite).

**Domain behavior** — `HistoricalDateTests` (7), `CompanyTests`,
`PricePointTests`, `NewsArticleTests`, `NewsRelevanceTests` (2),
`SecFilingTests` (2), `NullNewsProviderTests` (1).

## Conventions

- Fakes over mocks where behavior matters (`FixedNewsProviderFactory`,
  `StubMoves`, `FuncGeminiStub`, `Disabled*` stubs); Moq for boundary
  verification.
- InMemory database per test class instance (GUID-named); job-runner
  harness uses one store name with a fresh context per scope, mirroring
  production scoping (see caveat below).
- Thresholds are pinned and require re-measurement to change (e.g.
  embedding cosine 0.75); version stamps compared, not just shapes.

## Frontend

No unit-test project; CI enforces `oxlint` + `tsc -b && vite build`.
Contract safety comes from the shared `types.ts` shapes and the backend
`ApiContractTests` pinning the payloads the UI consumes.

## Known caveats, documented not hidden

- InMemory stores ignore column widths, so the Exchange truncation class
  is pinned at the code-contract level (mapped codes + clamps), not at
  the SQL level.
- Fixed (2026-09-10): the job-runner harness shared one `DbContext`
  between its polling loop and the background runner, flaking on CI with
  EF's concurrency-detector error. Each scope now gets its own context
  over the same InMemory store; the formerly flaky timeout test passes
  10/10 consecutive runs.
