import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { api } from '../lib/api';
import { fmtDate, fmtPct } from '../lib/format';
import { newsSourceLabel, type NewsSource } from '../types';
import { Alert, AlertDescription, AlertTitle } from '../components/ui/alert';
import { Badge } from '../components/ui/badge';
import { Button, buttonVariants } from '../components/ui/button';
import { Card, CardContent } from '../components/ui/card';
import { EmptySection, ErrorState, LoadingDossier } from '../components/StateBlocks';
import { MoveDrawer } from '../components/MoveDrawer';
import { MovesTimeline } from '../components/MovesTimeline';

function normalizeSource(raw: string | null): NewsSource {
  if (raw === 'alphavantage') return 'alphavantage';
  if (raw === 'marketaux') return 'marketaux';
  return 'gdelt';
}

export default function Moves() {
  const [params, setParams] = useSearchParams();
  const symbol = params.get('symbol')?.trim() ?? '';
  const date = params.get('date')?.trim() ?? '';
  const newsSource = normalizeSource(params.get('newsSource'));
  const [selected, setSelected] = useState<string | null>(null);
  const [prevKey, setPrevKey] = useState('');
  const paramsKey = `${symbol}|${date}|${newsSource}`;
  if (paramsKey !== prevKey) {
    // New investigation: clear the open drawer during render (no effect needed).
    setPrevKey(paramsKey);
    setSelected(null);
  }

  const query = useQuery({
    queryKey: ['moves', symbol.toUpperCase(), date, newsSource],
    queryFn: () => api.moves(symbol, date, newsSource),
    enabled: symbol !== '' && date !== '',
    staleTime: 5 * 60_000,
  });

  useEffect(() => {
    document.title = symbol && date
      ? `${symbol.toUpperCase()} · 100 days to ${fmtDate(date)} — Stock Time Machine`
      : 'Stock Time Machine';
  }, [symbol, date]);

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
      <div className="space-y-4">
        <p className="text-sm text-fg-muted" aria-busy="true">
          Analyzing the 100 trading days before {fmtDate(date)} — detecting significant movements…
        </p>
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
                  onSelect={(d) => setSelected((cur) => (cur === d ? null : d))}
                />
              </CardContent>
            </Card>
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
              onClose={() => setSelected(null)}
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
