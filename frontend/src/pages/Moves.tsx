import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { API_BASE, ApiError, api } from '../lib/api';
import { recordInvestigation } from '../lib/recentInvestigations';
import { fmtDate, fmtPct } from '../lib/format';
import { whyNoMoves } from '../lib/whyEmpty';
import { newsSourceLabel, type MovesResponse, type NarrativesResponse, type NewsSource } from '../types';
import { Alert, AlertDescription, AlertTitle } from '../components/ui/alert';
import { Badge } from '../components/ui/badge';
import { Button, buttonVariants } from '../components/ui/button';
import { Card, CardContent } from '../components/ui/card';
import { EmptySection, ErrorState, LoadingDossier, ReconstructionProgress, type StageEvent } from '../components/StateBlocks';
import { AiBriefBlock } from '../components/AiBriefBlock';
import { ConcludeNote } from '../components/ConcludeNote';
import { CutoffRule } from '../components/CutoffRule';
import { SectionHeading } from '../components/SectionHeading';
import { GuidedTour } from '../components/GuidedTour';
import { MethodLink } from '../components/MethodLink';
import { NextSteps } from '../components/NextSteps';
import { MoveDrawer } from '../components/MoveDrawer';
import { MovesTimeline } from '../components/MovesTimeline';
import { NarrativeTopics } from '../components/NarrativeTopics';

function normalizeSource(raw: string | null): NewsSource {
  if (raw === 'alphavantage') return 'alphavantage';
  if (raw === 'marketaux') return 'marketaux';
  return 'gdelt';
}

const MOVES_STAGES = [
  { key: 'detecting', label: 'Detecting key movements' },
  { key: 'evidence', label: 'Attaching evidence to each move' },
  { key: 'embedding', label: 'Embedding articles for grouping' },
  { key: 'clustering', label: 'Clustering narrative threads' },
  { key: 'briefing', label: 'Writing AI briefs for the largest threads' },
];

/**
 * Persisted-job moves flow: POST a background job (or reattach to the stored
 * one for this investigation), then follow its stream. Refresh/remount
 * reattaches to the same job_id instead of spawning duplicate work; only an
 * explicit retry (nonce) starts a fresh job. The job outlives every
 * connection by design — disconnects never cancel persisted runs.
 */
function jobStorageKey(symbol: string, date: string, newsSource: NewsSource) {
  return `stm:job:${symbol.toUpperCase()}|${date}|${newsSource}`;
}

function useMovesStream(symbol: string, date: string, newsSource: NewsSource, nonce: number) {
  const [stages, setStages] = useState<StageEvent[]>([]);
  const [moves, setMoves] = useState<MovesResponse | null>(null);
  const [narratives, setNarratives] = useState<NarrativesResponse | null>(null);
  const [error, setError] = useState<unknown>(null);

  useEffect(() => {
    if (symbol === '' || date === '') return;
    setStages([]);
    setMoves(null);
    setNarratives(null);
    setError(null);

    let cancelled = false;
    let es: EventSource | null = null;
    let settled = false;
    const key = jobStorageKey(symbol, date, newsSource);

    const onStage = (e: Event) => {
      try {
        const s = JSON.parse((e as MessageEvent).data) as StageEvent;
        setStages((prev) => [...prev.filter((p) => p.stage !== s.stage), s]);
      } catch {
        /* ignore malformed stage frames */
      }
    };
    const onMoves = (e: Event) => {
      try {
        setMoves(JSON.parse((e as MessageEvent).data) as MovesResponse);
      } catch {
        setError(new Error('The moves response could not be read.'));
        if (es) es.close();
      }
    };
    const onNarratives = (e: Event) => {
      try {
        setNarratives(JSON.parse((e as MessageEvent).data) as NarrativesResponse);
        settled = true;
      } catch {
        setError(new Error('The narratives response could not be read.'));
      } finally {
        if (es) es.close();
      }
    };
    const onError = (e: Event) => {
      if (e instanceof MessageEvent && e.data) {
        try {
          const problem = JSON.parse(e.data) as { detail?: string };
          setError(new ApiError(problem.detail ?? 'Request failed', 500, problem));
        } catch {
          setError(new Error('Request failed'));
        }
        if (es) es.close();
      } else if (!settled) {
        setError(new ApiError('The investigation service is unreachable. Check that the backend is running and try again.', 0, null));
        if (es) es.close();
      }
    };

    const attach = (jobId: string) => {
      if (cancelled) return;
      try {
        sessionStorage.setItem(key, jobId);
      } catch {
        /* private mode: reattach simply won't survive refresh */
      }
      es = new EventSource(`${API_BASE}/api/timemachine/moves/stream/${encodeURIComponent(jobId)}`);
      es.addEventListener('stage', onStage);
      es.addEventListener('moves', onMoves);
      es.addEventListener('narratives', onNarratives);
      es.addEventListener('error', onError);
    };

    const boot = async () => {
      try {
        let jobId: string | null = null;
        try {
          jobId = sessionStorage.getItem(key);
        } catch {
          jobId = null;
        }
        // Explicit retry always starts fresh; otherwise reattach.
        if (!jobId || nonce > 0) {
          const created = await api.createMovesJob({ symbol, date, newsSource });
          jobId = created.jobId;
        }
        attach(jobId);
      } catch (e) {
        if (!cancelled) setError(e);
      }
    };
    boot();

    return () => {
      cancelled = true;
      settled = true;
      // Closing our stream changes nothing server-side: the persisted job
      // keeps running and we reattach to it on remount.
      if (es) es.close();
    };
  }, [symbol, date, newsSource, nonce]);

  return { stages, moves, narratives, error };
}

/**
 * Plain-words explainer for the uncertainty score. Numbers come from the
 * deterministic engine; the copilot only translates them — no new math.
 */
function UncertaintyExplainer({
  symbol,
  date,
  newsSource,
}: {
  symbol: string;
  date: string;
  newsSource: NewsSource;
}) {
  const [brief, setBrief] = useState<import('../types').ClusterBrief | null>(null);
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);

  if (brief) return <AiBriefBlock brief={brief} context="plain-words reading of the measured components" />;

  return (
    <div>
      <button
        type="button"
        disabled={busy}
        onClick={() => {
          setBusy(true);
          setFailed(false);
          api
            .copilot('explain-uncertainty', { symbol, date, newsSource })
            .then((r) => {
              if (r.brief) setBrief(r.brief);
              else setFailed(true);
            })
            .catch(() => setFailed(true))
            .finally(() => setBusy(false));
        }}
        className="text-xs underline decoration-dotted underline-offset-2 hover:text-fg disabled:opacity-50"
      >
        {busy ? 'Explaining…' : 'Explain this score in plain words'}
      </button>
      {failed && !busy && (
        <p className="mt-1 text-xs text-fg-dim">No explainer available — the components above are the full story.</p>
      )}
    </div>
  );
}

export default function Moves() {
  const [params, setParams] = useSearchParams();
  const symbol = params.get('symbol')?.trim() ?? '';
  const date = params.get('date')?.trim() ?? '';
  const newsSource = normalizeSource(params.get('newsSource'));
  // Drawer selection is URL state (`?move=YYYY-MM-DD`) so a move is shareable
  // and survives reloads. `move` stays out of paramsKey: toggling the drawer
  // must not reset the investigation.
  const [selected, setSelected] = useState<string | null>(() => params.get('move'));
  const [prevKey, setPrevKey] = useState('');
  const paramsKey = `${symbol}|${date}|${newsSource}`;
  if (paramsKey !== prevKey) {
    // New investigation: adopt any deep-linked move during render (no effect needed).
    setPrevKey(paramsKey);
    setSelected(params.get('move'));
  }

  const syncMoveParam = (next: string | null) => {
    setSelected(next);
    const nextParams: Record<string, string> = { symbol, date, newsSource };
    if (next) nextParams.move = next;
    setParams(nextParams, { replace: true });
  };

  const [nonce, setNonce] = useState(0);
  const stream = useMovesStream(symbol, date, newsSource, nonce);

  useEffect(() => {
    document.title = symbol && date
      ? `${symbol.toUpperCase()} · 100 days to ${fmtDate(date)} — Stock Time Machine`
      : 'Stock Time Machine';
    if (symbol && date) recordInvestigation(symbol, date, newsSource);
  }, [symbol, date, newsSource]);

  if (symbol === '' || date === '') {
    return (
      <EmptySection
        title="No investigation selected"
        body="Choose a company and a historical date to open its 100-day investigation window."
        action={
          <Link to="/investigate" className={buttonVariants()}>
            Start an investigation
          </Link>
        }
      />
    );
  }

  if (!stream.moves) {
    return (
      <div className="space-y-4" aria-busy="true">
        <p className="text-sm text-fg-muted">
          Analyzing the 100 trading days before {fmtDate(date)}…
        </p>
        <ReconstructionProgress
          stages={stream.stages}
          defs={MOVES_STAGES}
          title="Detecting movements, attaching evidence, clustering threads — live."
          footnote="Every row is a real pipeline step: deterministic detection, per-move evidence, embeddings with live counts, then AI briefs."
        />
        {stream.error ? (
          <ErrorState
            error={stream.error}
            fallback="The 100-day analysis could not be completed."
            onRetry={() => setNonce((n) => n + 1)}
            backTo="/investigate"
          />
        ) : (
          <LoadingDossier />
        )}
      </div>
    );
  }

  const data = stream.moves;
  // Threads stream in after moves: keep the section live until they arrive.
  const threadsView = {
    data: stream.narratives ?? undefined,
    isPending: !stream.narratives && !stream.error,
    isError: !!stream.error && !stream.narratives,
    error: stream.error,
    refetch: () => setNonce((n) => n + 1),
  };
  const s = data.summary;
  const selectedMove = data.keyMoves.find((m) => m.date === selected) ?? null;
  const selectedRank = selectedMove ? data.keyMoves.indexOf(selectedMove) + 1 : 0;

  return (
    <div className="space-y-8">
      <GuidedTour page="/moves" />
      <section aria-labelledby="moves-title" className="space-y-3">
        <div className="flex flex-wrap items-center gap-2">
          <h1 id="moves-title" className="font-display text-3xl font-semibold tracking-tight">
            {data.company.name}
          </h1>
          <Badge variant="secondary" className="font-mono">{data.company.symbol}</Badge>
          {data.company.exchange && <Badge variant="outline">{data.company.exchange}</Badge>}
        </div>
        <p className="text-lg text-fg-muted">
          The 100 trading days leading up to the decision —{' '}
          <strong className="text-fg">{fmtDate(data.decisionDate)}</strong>.
        </p>
        <CutoffRule date={data.decisionDate} label="Lens ends here — decision moment" />
        <div>
          <Link
            to={`/compare?symbols=${data.company.symbol}&date=${data.decisionDate}&newsSource=${newsSource}`}
            className={buttonVariants({ variant: 'outline', size: 'sm' })}
          >
            Compare this window vs another company
          </Link>
        </div>
        <p className="text-xs text-fg-dim">
          Movements detected deterministically (see Methodology); each carries only evidence
          available by its own date.
        </p>
        <div className="flex flex-wrap items-center gap-2" aria-label="News source selection">
          <span className="text-xs text-fg-dim">News evidence from:</span>
          {(['gdelt', 'alphavantage', 'marketaux'] as NewsSource[]).map((s) => (
            <Button
              key={s}
              size="sm"
              variant={newsSource === s ? 'default' : 'outline'}
              onClick={() =>
                setParams(
                  { symbol: data.company.symbol, date: data.decisionDate, newsSource: s },
                  { replace: true },
                )
              }
              aria-pressed={newsSource === s}
            >
              {newsSourceLabel(s)}
            </Button>
          ))}
        </div>
      </section>

      {!stream.narratives && !stream.error && (
        <div aria-busy="true">
          <ReconstructionProgress
            stages={stream.stages}
            defs={MOVES_STAGES}
            title="Moves are in — now clustering threads and writing briefs, live."
            footnote="Embeddings report live counts; briefs arrive per thread."
          />
        </div>
      )}

      <section aria-label="Decision uncertainty" data-tour="uncertainty" className="space-y-2">
        <Card>
          <CardContent className="space-y-2 pt-6">
            <div className="flex flex-wrap items-baseline gap-x-3">
              <SectionHeading kicker="Decide with context">Decision uncertainty</SectionHeading>
              <span className="font-mono text-2xl font-semibold tabular" aria-label={`Uncertainty score ${data.uncertainty.score} out of 100`}>
                {data.uncertainty.score.toFixed(1)}
              </span>
              <span className="text-xs text-fg-dim">/ 100 · higher means thinner or more conflicting evidence</span>
            </div>
            <ul className="space-y-1 text-sm">
              {data.uncertainty.components.map((c) => (
                <li key={c.name} className="flex flex-wrap gap-x-2">
                  <span className="font-mono tabular text-fg-muted">
                    {(c.weight * 100).toFixed(0)}% × {c.value.toFixed(3)}
                  </span>
                  <span className="font-medium">{c.name}</span>
                  <span className="text-xs text-fg-dim">— {c.detail}</span>
                </li>
              ))}
            </ul>
            <p className="text-xs text-fg-dim">
              Transparent formula, no hidden inputs — <MethodLink anchor="decision-uncertainty-index" /> Never investment advice.
            </p>
            <UncertaintyExplainer symbol={symbol} date={data.decisionDate} newsSource={newsSource} />
          </CardContent>
        </Card>
      </section>

      {!s.sufficientHistory ? (
        <EmptySection
          title="Insufficient history"
          body={`Only ${s.tradingDays} trading days of market data exist before ${fmtDate(data.decisionDate)}. At least 30 are required for the 100-day lens.`}
          action={
            <Link to="/investigate" className={buttonVariants({ variant: 'outline', size: 'sm' })}>
              Try another date
            </Link>
          }
        />
      ) : (
        <>
          <section aria-label="Window summary" data-tour="timeline" className="space-y-4">
            <Card>
              <CardContent className="space-y-4 pt-6">
                <div className="flex flex-wrap items-end gap-x-8 gap-y-2">
                  <div>
                    <p className="text-xs text-fg-dim">100-day cumulative return</p>
                    <p className="font-mono text-3xl font-semibold tabular">
                      {s.cumulativeReturnPct > 0 ? '+' : ''}{s.cumulativeReturnPct.toFixed(2)}%
                    </p>
                  </div>
                  <dl className="grid grid-cols-2 gap-x-8 gap-y-1 text-sm sm:grid-cols-4">
                    <div><dt className="text-xs text-fg-dim">Volatility (ann.)</dt><dd className="font-mono tabular">{s.volatility.toFixed(2)}%</dd></div>
                    <div><dt className="text-xs text-fg-dim">Max drawdown</dt><dd className="font-mono tabular">{fmtPct(s.maxDrawdownPct)}</dd></div>
                    <div><dt className="text-xs text-fg-dim">Best day</dt><dd className="font-mono tabular">{s.bestDay ? `${fmtDate(s.bestDay)} (${fmtPct(s.bestDayReturnPct)})` : '—'}</dd></div>
                    <div><dt className="text-xs text-fg-dim">Worst day</dt><dd className="font-mono tabular">{s.worstDay ? `${fmtDate(s.worstDay)} (${fmtPct(s.worstDayReturnPct)})` : '—'}</dd></div>
                  </dl>
                </div>
                <MovesTimeline
                  prices={data.windowPrices}
                  moves={data.keyMoves}
                  decisionDate={data.decisionDate}
                  selectedDate={selected}
                  onSelect={(d) => syncMoveParam(selected === d ? null : d)}
                  regimes={data.regimes}
                />
              </CardContent>
            </Card>
          </section>

          <section aria-label="Narrative threads" data-tour="threads" className="space-y-2">
            <NarrativeTopics
              query={threadsView}
              symbol={symbol}
              date={data.decisionDate}
              newsSource={newsSource}
            />
          </section>

          {data.keyMoves.length === 0 && (
            <Alert>
              <AlertTitle>No standout movements</AlertTitle>
              <AlertDescription>{whyNoMoves()}</AlertDescription>
            </Alert>
          )}

          {selectedMove && (
            <MoveDrawer
              move={selectedMove}
              rank={selectedRank}
              evidence={data.evidenceByDate[selectedMove.date]}
              symbol={data.company.symbol}
              newsSource={newsSource}
              onClose={() => syncMoveParam(null)}
            />
          )}

          <p className="text-xs text-fg-dim">
            Scoring: 0.5·return surprise + 0.3·volume anomaly + 0.2·range break (full formula in
            Methodology). Window ends {fmtDate(data.decisionDate)}; each move carries only evidence
            available by its own date.
            Figures use raw closes; splits and dividends are not adjusted.
          </p>

          <section aria-label="Suggested next steps" className="space-y-2">
            <NextSteps data={data} newsSource={newsSource} />
          </section>

          <section aria-label="Conclude" id="conclude" className="space-y-2 scroll-mt-20">
            <ConcludeNote
              storageKey={`stm:note:${data.company.symbol}|${data.decisionDate}|${newsSource}`}
              symbol={data.company.symbol}
              date={data.decisionDate}
              newsSource={newsSource}
              citations={[
                ...data.keyMoves.map((m, i) => ({
                  id: `move ${m.date}`,
                  label: `#${i + 1} ${m.date} ${m.dailyReturnPct.toFixed(2)}%`,
                })),
                ...(stream.narratives?.topics.map((t) => ({
                  id: `thread ${t.labelTerms.join(' · ')}`,
                  label: t.representativeTitle,
                })) ?? []),
              ]}
            />
          </section>
        </>
      )}
    </div>
  );
}
