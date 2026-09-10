# Point-in-time methodology

The central methodological problem: a historical stock analysis can
accidentally use information that was not available at the date being
investigated. This system is built around three distinct moments that must
never mix.

## Three moments

- **T — the investigation date.** The date the researcher picks.
- **Knowable by T** — every price, filing, article, and post whose source
  timestamp is at or before the cutoff: T at 23:59:59 US/Eastern, converted
  to UTC (`TemporalBoundary.GetCutoffUtc`).
- **After T** — everything later: realized prices, subsequent filings,
  post-cutoff coverage. Displayed only in quarantined aftermath panels,
  never fed back into analysis.

The corpus has documented holes (see [limitations](limitations.md)), so the
honest claim is always "available up to a defined point-in-time cutoff" —
never "exactly as it stood."

## The day-boundary rule

Some evidence carries calendar days, not timestamps: SEC filing dates and
GDELT story dates are stored as midnight UTC. A row dated D+1 at midnight
UTC *precedes* the end-of-day-Eastern instant cutoff for D — so the instant
filter alone would leak the next day's evidence into D's investigation.
Both news reads therefore apply a second, calendar-day bound
(`StartOfDayAfterUtc`): day-granularity rows dated after the investigation
date are excluded even though their midnight timestamp passes the instant
cutoff. True-timestamp rows (Alpha Vantage, MarketAux) keep the instant
rule — a real 22:00 Eastern article is knowable the same evening.

Worked incident: a June 1 move displayed June 2 stories; after the fix,
zero post-date rows remained, verified live.

## Per-layer timestamps

| Layer | Timestamp | Bound |
|---|---|---|
| Prices | trading-day date | `Date <= T` (bars knowable at end of day) |
| SEC filings | filing date, midnight UTC | calendar day ≤ T |
| GDELT Cloud news | story date, midnight UTC | calendar day ≤ T |
| Alpha Vantage / MarketAux news | true publication instant | instant ≤ cutoff |
| Social posts | creation instant | instant ≤ cutoff, sliced per move ±7d |
| Regulatory evidence | filing date | 30-day window before each move (`reg-v1`) |
| Reaction closes | trading dates after the move | aftermath only, never evidence |
| Derived features (regimes, sentiment, uncertainty) | computed from the above | inherit the strictest input bound |

## Arrival and freezing

Each move records when every layer *first* carried something knowable about
it (first-seen per layer, lags vs the earliest layer; silent layers render
unknown, never zero). Significant moves are frozen with their pre-peak
narrative threads, regime path, sentiment, and evidence counts into registry
cases (`hcp-v1`) that survive job pruning. Threads vote only with members
dated on/before the peak (or dateless legacy rows); resemblance pooling
excludes post-peak members.

## Missing data

Absence is a named state, never a zero: layers read missing/empty/unavailable;
triggers requiring missing inputs do not fire; empty results distinguish
cache-only empty, live-fetch-zero, and fetch-failed. See
[limitations](limitations.md) for verified coverage holes.

## Open directions

What a larger program would change, stated as open questions rather than a
roadmap: 1,000+ cases across sectors would turn aftermath panels from
anecdotes into distributions with calibratable thresholds; dense
retail-attention series would add the diffusion leg the arrival map was
designed for; re-running frozen windows as the corpus grows would measure
detection stability over time — the version stamps exist precisely to make
that study reproducible.
