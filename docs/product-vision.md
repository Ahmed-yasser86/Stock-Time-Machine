# Project & Product Vision

Stock Time Machine is a **research-driven project with a real product
implementation**. The research asks how historical information
environments can be reconstructed with temporal integrity: what was
knowable at a moment, what narratives were visible, how information
arrived, and what evidence supported what. The product — a working
system with a UI and API — operationalizes those ideas and tests them
against real corpora every day. That is why the product is designed
around cutoffs, provenance, reproducibility, deterministic processing,
and uncertainty: the research questions demand them, and the product
proves they can be engineered.

The Stock Time Machine began with a simple question: **what could an
observer have known about a company at a particular moment in time?**
That question leads past historical prices. A meaningful answer needs
the information environment around those prices — news, filings,
narratives, the arrival of information, competing interpretations, and
the evidence available before the cutoff. The system already provides
the foundation for that reconstruction. The trajectory from here is to
make the environment richer, longitudinal, comparative, multi-channel,
and researchable — until the product reconstructs not only historical
snapshots, but the evolution, diffusion, comparison, and interpretation
of information environments through time.

## The foundation already built

The working core, in production today: point-in-time evidence across
prices, filings, news, and discussion, each item cutoff-filtered with
post-cutoff material quarantined to aftermath panels; narrative threads
clustered from relevance-admitted articles, inspectable to canonical
URLs; two-company comparison with shared briefs and cross-thread pairs;
per-layer arrival stamps recording when each kind of information first
appeared; four news transports plus SEC, Reddit archive, and live quotes
behind fail-soft provider seams; and an evidence-grounded copilot whose
briefs cite triggering threads and whose methodology answers cite
sections. This foundation is what every section below builds on.

## 1. From historical snapshots to historical intelligence

The cutoff reconstruction is the foundation of the entire product: not
"we have historical data" but "we can re-establish what an information
environment looked like under a historical bound — what had arrived,
what narratives were visible, what evidence existed." Increasing the
resolution of that capability is the natural next layer: richer
per-item provenance, corpus and version history at fetch time, broader
source coverage, and longer horizons where provider coverage thins
(see [limitations](limitations.md) for current corpus bounds).

## 2. From threads to narrative lifecycles

The thread-clustering system already identifies coherent narratives
within an investigation window ([thread-clustering](thread-clustering.md)).
The larger arc follows those narratives through time — emergence,
growth, persistence, fragmentation, competing framings, merging,
disappearance, changing evidence — resolving thread identity across
successive as-of dates from stored rows only, under the same
frozen-evidence discipline. The conceptual transition is **from
reconstructing a snapshot of narratives to reconstructing narrative
lifecycles**, so a researcher can ask how a story evolved rather than
only what a window contained.

## 3. From company comparison to information-environment comparison

The existing two-company comparison already joins threads and briefs
across companies. The deeper question it points at is not which stock
performed better but **how different companies inhabited the same
information environment differently** — narrative composition, evidence
arrival, information exposure, attention, competing narratives, market
context — still descriptive, still without pooled verdicts, extending
the current compare contract to more companies and more dimensions.

## 4. From evidence links to structured provenance

Outputs already connect to underlying evidence: canonical URLs, verdict
metadata, cited threads and sections. Formalizing that strength into a
machine-readable evidence model — observed evidence vs reconstructed
relationships vs model interpretation vs quantified uncertainty — makes
historical intelligence increasingly inspectable, letting API consumers
filter by evidence grade. Provenance becomes a queryable property of
the system rather than a property of its prose.

## 5. From multiple sources to an information ecosystem

The multi-source architecture (reporting, regulatory, discussion,
prices) is the foundation for reconstructing a broader information
ecosystem: transcripts and interviews, analyst commentary, company
communications, blogs, podcasts, public video media. The product idea
is not source accumulation — different channels reveal different
dimensions of an environment (reporting, interpretation, discussion,
disagreement, amplification, circulation), each with per-channel
provenance preserved. The trajectory is **from a multi-source
news/data system toward a multi-channel information-environment
reconstruction system**.

## 6. From arrival data to information diffusion and attention

Arrival stamps already record when each information layer first
appeared — the temporal foundation. The next layer studies how
information moved: event → reporting → commentary → discussion →
amplification → competing narrative → changing attention, analyzed as
temporal sequence, co-occurrence, diffusion, and attention. An
`ISearchInterestProvider` port already exists for the attention axis,
reserved for an official credentialed source. Diffusion analysis stays
within the project's non-causal methodology: sequence and co-occurrence
are labeled as such.

## 7. From historical information to decision reconstruction

Because the product reconstructs what was knowable at a point in time,
a natural next question is what decision context existed for someone
acting at that moment — the informational constraints and available
evidence surrounding historical decisions. This extends the current
copilot (which already reviews conclusions against cited evidence) into
historical decision-environment reconstruction: a way to study how
decisions related to their information, distinct in kind from
recommendation.

## 8. From product API to research platform

The system already produces structured historical cases, threads,
evidence relationships, and arrival data. Exposing these as stable,
versioned research corpora turns the product into infrastructure:
researchers studying narratives, diffusion, attention, uncertainty, and
historical decision-making get timestamped, version-stamped material
built for computational work. The arc is **from an application that
answers historical questions to infrastructure that enables researchers
to study historical information environments**.

## 9. From copilot to an AI research interface

The copilot's bounded actions over already-retrieved evidence point
toward a fuller interface: a researcher interrogating the reconstructed
environment — which narratives dominated a company on a date, how two
companies' environments differed, which pre-cutoff evidence supported a
narrative, how a narrative evolved across dates. The AI's value is
making the environment interrogable while evidence, provenance,
temporal bounds, and uncertainty stay attached to every answer. The
constraint that gives this power is scope: the interface never sees a
broader context than the evidence the question is allowed to see.

## 10. Beyond markets: long-term horizon

"What was knowable then" generalizes in principle — technology
developments, policy, geopolitical and economic events, public
narratives, industries. The current product is intentionally grounded
in markets, and its implementation is market-coupled at every layer, so
this remains a conceptual horizon that the architecture protects
(ports, frozen schemas, no market logic in generic machinery) rather
than a direction being pursued.

## Principles that carry forward

These methodology constraints are what make the trajectory above
trustworthy, and they travel with every extension: temporally grounded
(cutoffs on everything), evidence-traceable (citations, not assertions),
non-predictive, non-advisory, non-causal, provenance-preserving,
deterministic where appropriate, and degradation-aware (honest empty
states over invented data).
