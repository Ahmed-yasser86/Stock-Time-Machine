import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { api } from '../lib/api';
import { recordInvestigation } from '../lib/recentInvestigations';
import { fmtDate, fmtPct } from '../lib/format';
import { newsSourceLabel, type NewsSource } from '../types';
import { Alert, AlertDescription, AlertTitle } from '../components/ui/alert';
import { Badge } from '../components/ui/badge';
import { Button, buttonVariants } from '../components/ui/button';
import { Card, CardContent } from '../components/ui/card';
import { EmptySection, ErrorState, LoadingDossier } from '../components/StateBlocks';
import { MoveDrawer } from '../components/MoveDrawer';
import { MovesTimeline } from '../components/MovesTimeline';
import { NarrativeTopics } from '../components/NarrativeTopics';

function normalizeSource(raw: string | null): NewsSource {
  if (raw === 'alphavantage') return 'alphavantage';
  if (raw === 'marketaux') return 'marketaux';
  return 'gdelt';
}

/**
 * Staged progress for the two-phase analysis. Step states only — never fake
 * percentages: each step is waiting, active, or done.
 */
function AnalysisProgress({ movesDone, threadsDone }: { movesDone: boolean; threadsDone: boolean }) {
  const steps = [
    {
      label: 'Detecting key movements',
      hint: 'deterministic scan of the 100 trading days',
      state: movesDone ? 'done' : 'active',
    },
    {
      label: 'Clustering threads & writing AI briefs',
      hint: 'embeddings, article fetches, summaries — can take up to a minute',
      state: !movesDone ? 'waiting' : threadsDone ? 'done' : 'active',
    },
  ] as const;
  return (
    <ol aria-label="Analysis progress" className="space-y-1 text-sm">
      {steps.map((s) => (
        <li key={s.label} className="flex items-baseline gap-2" aria-current={s.state === 'active' ? 'step' : undefined}>
          <span aria-hidden="true">{s.state === 'done' ? '✓' : s.state === 'active' ? '◌' : '·'}</span>
          <span className={s.state === 'waiting' ? 'text-fg-dim' : 'text-fg'}>
            {s.label}
            <span className="text-xs text-fg-dim"> — {s.hint}</span>
            {s.state === 'active' ? '…' : ''}
          </span>
        </li>
      ))}
    </ol>
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

  const query = useQuery({
    queryKey: ['moves', symbol.toUpperCase(), date, newsSource],
    queryFn: () => api.moves(symbol, date, newsSource),
    enabled: symbol !== '' && date !== '',
    staleTime: 5 * 60_000,
  });

  // Owned here (not inside NarrativeTopics) so the page can stage progress:
  // moves first, threads + AI briefs second. Narratives run only after moves
  // succeed, against the same decision date the moves call resolved.
  const narrativesQuery = useQuery({
    queryKey: ['narratives', symbol.toUpperCase(), date, newsSource],
    queryFn: () => api.narratives(symbol, date, newsSource),
    enabled: query.isSuccess,
    staleTime: 5 * 60_000,
  });

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

  if (query.isPending) {
    return (
      <div className="space-y-4" aria-busy="true">
        <p className="text-sm text-fg-muted">
          Analyzing the 100 trading days before {fmtDate(date)}…
        </p>
        <AnalysisProgress movesDone={false} threadsDone={false} />
        <LoadingDossier />
      </div>
    );
  }

  if (query.isError) {
    return (
      <ErrorState
        error={query.error}
        fallback="The 100-day analysis could not be completed."
        onRetry={() => query.refetch()}
        backTo="/investigate"
      />
    );
  }

  const data = query.data;
  const s = data.summary;
  const selectedMove = data.keyMoves.find((m) => m.date === selected) ?? null;
  const selectedRank = selectedMove ? data.keyMoves.indexOf(selectedMove) + 1 : 0;

  return (
    <div className="space-y-8">
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

      {narrativesQuery.isPending && (
        <div aria-busy="true">
          <AnalysisProgress movesDone={true} threadsDone={false} />
        </div>
      )}

      <section aria-label="Decision uncertainty" className="space-y-2">
        <Card>
          <CardContent className="space-y-2 pt-6">
            <div className="flex flex-wrap items-baseline gap-x-3">
              <h2 className="text-lg font-semibold">Decision uncertainty</h2>
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
              Transparent formula, no hidden inputs — see Methodology. Never investment advice.
            </p>
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
          <section aria-label="Window summary" className="space-y-4">
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

          <section aria-label="Narrative threads" className="space-y-2">
            <NarrativeTopics
              query={{
                data: narrativesQuery.data,
                isPending: narrativesQuery.isPending,
                isError: narrativesQuery.isError,
                error: narrativesQuery.error,
                refetch: () => narrativesQuery.refetch(),
              }}
            />
          </section>

          {data.keyMoves.length === 0 && (
            <Alert>
              <AlertTitle>No standout movements</AlertTitle>
              <AlertDescription>
                Nothing in this window cleared the significance bar — an unusually calm market
                is itself information about the decision context.
              </AlertDescription>
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
        </>
      )}
    </div>
  );
}
