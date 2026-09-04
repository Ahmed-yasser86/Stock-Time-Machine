import { Suspense, lazy, useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { useMutation } from '@tanstack/react-query';
import {
  AlertTriangle,
  ArrowDownRight,
  ArrowUpRight,
  ExternalLink,
  FlaskConical,
  Minus,
} from 'lucide-react';
import { API_BASE, ApiError, api, apiErrorMessage } from '../lib/api';
import { recordInvestigation } from '../lib/recentInvestigations';
import {
  direction,
  fmtDate,
  fmtDateShort,
  fmtDateTimeUtc,
  fmtMoney,
  fmtPct,
  fmtSignedMoney,
  fmtVolume,
  todayLocal,
} from '../lib/format';
import {
  SIMULATION_DISCLAIMER,
  newsSourceLabel,
  type NewsSource,
  type SnapshotResponse,
} from '../types';
import { Alert, AlertDescription, AlertTitle } from '../components/ui/alert';
import { Badge } from '../components/ui/badge';
import { Button, buttonVariants } from '../components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../components/ui/card';
import { Input } from '../components/ui/input';
import { Label } from '../components/ui/label';
import { Separator } from '../components/ui/separator';
import {
  Table,
  TableBody,
  TableCaption,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '../components/ui/table';
import { EmptySection, ErrorState, LoadingDossier, ReconstructionProgress, type StageEvent } from '../components/StateBlocks';
import { EvidenceStream } from '../components/EvidenceStream';
import { GuidedTour } from '../components/GuidedTour';
import { TemporalRuler } from '../components/TemporalRuler';
import type { ChartMarker } from '../components/PriceChart';
import { Skeleton } from '../components/ui/skeleton';

const PriceChart = lazy(() => import('../components/PriceChart').then((m) => ({ default: m.PriceChart })));

function ChartFallback() {
  return <Skeleton className="h-[220px] w-full" aria-label="Loading chart" />;
}

function normalizeSource(raw: string | null): NewsSource {
  if (raw === 'alphavantage') return 'alphavantage';
  if (raw === 'marketaux') return 'marketaux';
  return 'gdelt';
}

function TrendIcon({ value }: { value: number | null | undefined }) {
  const d = direction(value);
  if (d === 'gain') return <ArrowUpRight className="size-4 text-gain" aria-label="Up" />;
  if (d === 'loss') return <ArrowDownRight className="size-4 text-loss" aria-label="Down" />;
  return <Minus className="size-4 text-fg-dim" aria-label="Unchanged" />;
}

function Simulation({
  symbol,
  entryDate,
  entryClose,
  onExit,
}: {
  symbol: string;
  entryDate: string;
  entryClose: number;
  onExit: (date: string | null) => void;
}) {
  const [amount, setAmount] = useState('10000');
  const [exit, setExit] = useState('');
  const [formError, setFormError] = useState<string | null>(null);
  const sim = useMutation({ mutationFn: api.runSimulation });
  const result = sim.data ?? null;

  useEffect(() => {
    // Links the simulation to the outcome chart: the exit lands as a marker.
    onExit(result?.exitDate ?? null);
  }, [result, onExit]);

  function submit() {
    setFormError(null);
    const amt = Number(amount);
    if (!Number.isFinite(amt) || amt <= 0) {
      setFormError('Enter an investment amount greater than zero.');
      return;
    }
    if (exit && exit < entryDate) {
      setFormError('Exit date must be on or after the entry date.');
      return;
    }
    sim.mutate({ symbol, entryDate, amount: amt, exitDate: exit || undefined });
  }

  const usedLatestExit = result && !exit;

  return (
    <Card aria-label="Hypothetical investment simulation">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <FlaskConical className="size-4 text-primary" aria-hidden="true" /> Hypothetical outcome calculator
        </CardTitle>
        <CardDescription>
          A separate analytical tool — not evidence, not what happened. Entry price is the real
          closing price on {fmtDate(entryDate)}: <span className="font-mono">{fmtMoney(entryClose)}</span>.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="grid gap-3 sm:grid-cols-3">
          <div className="space-y-2">
            <Label htmlFor="sim-amount">Investment amount (USD)</Label>
            <Input
              id="sim-amount"
              type="number"
              min="0"
              step="0.01"
              value={amount}
              onChange={(e) => setAmount(e.target.value)}
            />
          </div>
          <div className="space-y-2">
            <Label htmlFor="sim-exit">Exit date (optional)</Label>
            <Input
              id="sim-exit"
              type="date"
              min={entryDate}
              max={todayLocal()}
              value={exit}
              onChange={(e) => setExit(e.target.value)}
            />
          </div>
          <div className="flex items-end">
            <Button onClick={submit} disabled={sim.isPending} aria-busy={sim.isPending}>
              {sim.isPending ? 'Calculating…' : 'Calculate hypothetical outcome'}
            </Button>
          </div>
        </div>

        {formError && (
          <p role="alert" className="text-sm text-destructive">
            {formError}
          </p>
        )}
        {sim.isError && (
          <p role="alert" className="text-sm text-destructive">
            {apiErrorMessage(sim.error, 'The simulation could not be calculated.')}
          </p>
        )}

        {result && (
          <div className="space-y-2 rounded-lg border border-border p-4" aria-live="polite">
            <p className="text-sm text-fg-muted">
              If you had invested {fmtMoney(result.investmentAmount)} on {fmtDate(result.entryDate)}…
            </p>
            <dl className="grid grid-cols-2 gap-3 text-sm sm:grid-cols-3">
              <div>
                <dt className="text-xs text-fg-dim">Shares purchased</dt>
                <dd className="font-mono tabular">{result.sharesPurchased.toFixed(4)}</dd>
              </div>
              <div>
                <dt className="text-xs text-fg-dim">Entry price</dt>
                <dd className="font-mono tabular">{fmtMoney(result.entryPrice)}</dd>
              </div>
              <div>
                <dt className="text-xs text-fg-dim">Exit price</dt>
                <dd className="font-mono tabular">{result.exitPrice === null ? '—' : fmtMoney(result.exitPrice)}</dd>
              </div>
              <div>
                <dt className="text-xs text-fg-dim">Final value</dt>
                <dd className="font-mono tabular">{fmtMoney(result.finalValue)}</dd>
              </div>
              <div>
                <dt className="text-xs text-fg-dim">Gain / loss</dt>
                <dd className="flex items-center gap-1 font-mono tabular">
                  <TrendIcon value={result.finalValue - result.investmentAmount} />
                  {fmtSignedMoney(result.finalValue - result.investmentAmount)}
                </dd>
              </div>
              <div>
                <dt className="text-xs text-fg-dim">Return</dt>
                <dd className="flex items-center gap-1 font-mono tabular">
                  <TrendIcon value={result.returnPercentage} />
                  {fmtPct(result.returnPercentage)}
                </dd>
              </div>
            </dl>
            <p className="text-xs text-fg-dim">
              Exit: {result.exitDate ? fmtDate(result.exitDate) : '—'}
              {usedLatestExit ? ' (most recent available price).' : '.'} This transaction never
              occurred — it is a hypothetical calculation.
            </p>
          </div>
        )}

        <p className="text-xs text-fg-dim">{SIMULATION_DISCLAIMER}</p>
      </CardContent>
    </Card>
  );
}

function Dossier({ data }: { data: SnapshotResponse }) {
  const [params, setParams] = useSearchParams();
  const currentSource = normalizeSource(params.get('newsSource'));
  const hasMarket = data.recentPrices.length > 0;
  const close = hasMarket ? data.price.close : null;
  const [simExit, setSimExit] = useState<string | null>(null);

  const outcomeChange =
    data.outcome.price !== null && close !== null && close !== 0
      ? ((data.outcome.price - close) / close) * 100
      : null;

  const outcomeMarkers: ChartMarker[] = [
    ...data.outcome.filings.map((f) => ({ date: f.filedAt.slice(0, 10), label: `${f.formType} filed` })),
    ...(simExit ? [{ date: simExit, label: 'Simulation exit' }] : []),
  ];

  function switchSource(s: NewsSource) {
    setParams(
      { symbol: data.company.symbol, date: data.snapshotDate, newsSource: s },
      { replace: true },
    );
  }

  function moveToDate(d: string) {
    setParams(
      { symbol: data.company.symbol, date: d, newsSource: currentSource },
      { replace: false },
    );
  }

  return (
    <div className="space-y-8">
      <GuidedTour page="/snapshot" />
      {/* Identity + boundary */}
      <section aria-labelledby="dossier-title" className="space-y-3">
        <div className="flex flex-wrap items-center gap-2">
          <h1 id="dossier-title" className="font-display text-3xl font-semibold tracking-tight">
            {data.company.name}
          </h1>
          <Badge variant="secondary" className="font-mono">{data.company.symbol}</Badge>
          {data.company.exchange && <Badge variant="outline">{data.company.exchange}</Badge>}
        </div>
        <p className="text-lg text-fg-muted">
          The information environment as it existed on{' '}
          <strong className="text-fg">{fmtDate(data.snapshotDate)}</strong>.
        </p>
        <p className="text-xs text-fg-dim">
          Temporal cutoff: {fmtDateTimeUtc(data.cutoffUtc)} (23:59:59 US/Eastern on the selected
          date). Nothing with a later source timestamp appears below this line&apos;s sections.
        </p>
        <div className="flex flex-wrap items-center gap-2" aria-label="News source selection">
          <span className="text-xs text-fg-dim">News evidence from:</span>
          {(['gdelt', 'alphavantage', 'marketaux'] as NewsSource[]).map((s) => (
            <Button
              key={s}
              size="sm"
              variant={currentSource === s ? 'default' : 'outline'}
              onClick={() => switchSource(s)}
              aria-pressed={currentSource === s}
            >
              {newsSourceLabel(s)}
            </Button>
          ))}
        </div>
        {(hasMarket || data.outcome.prices.length > 0) && (
          <TemporalRuler
            history={data.recentPrices}
            outcome={data.outcome.prices}
            selectedDate={data.snapshotDate}
            onSelect={moveToDate}
          />
        )}
      </section>

      {data.warnings.length > 0 && (
        <Alert>
          <AlertTriangle />
          <AlertTitle>Coverage notes</AlertTitle>
          <AlertDescription>
            <ul className="list-disc space-y-1 pl-4">
              {data.warnings.map((w) => (
                <li key={w}>{w}</li>
              ))}
            </ul>
          </AlertDescription>
        </Alert>
      )}

      {/* Market context */}
      <section aria-label="Historical market context" data-tour="boundary" className="space-y-4">
        <div className="flex flex-wrap items-baseline gap-x-4 gap-y-1">
          <h2 className="text-lg font-semibold">Historical market context</h2>
          <Link
            to={`/moves?symbol=${encodeURIComponent(data.company.symbol)}&date=${data.snapshotDate}&newsSource=${currentSource}`}
            className="text-sm text-primary underline-offset-4 hover:underline"
          >
            Investigate the 100 days before this date →
          </Link>
        </div>
        {hasMarket ? (
          <Card>
            <CardContent className="space-y-4 pt-6">
              <div className="flex flex-wrap items-end gap-x-6 gap-y-2">
                <div>
                  <p className="text-xs text-fg-dim">Closing Price — {fmtDate(data.snapshotDate)}</p>
                  <p className="font-mono text-4xl font-semibold tabular">{fmtMoney(close)}</p>
                </div>
                <dl className="grid grid-cols-2 gap-x-6 gap-y-1 text-sm sm:grid-cols-4">
                  <div><dt className="text-xs text-fg-dim">Open</dt><dd className="font-mono tabular">{fmtMoney(data.price.open)}</dd></div>
                  <div><dt className="text-xs text-fg-dim">High</dt><dd className="font-mono tabular">{fmtMoney(data.price.high)}</dd></div>
                  <div><dt className="text-xs text-fg-dim">Low</dt><dd className="font-mono tabular">{fmtMoney(data.price.low)}</dd></div>
                  <div><dt className="text-xs text-fg-dim">Volume</dt><dd className="font-mono tabular">{fmtVolume(data.price.volume)}</dd></div>
                </dl>
              </div>
              <Suspense fallback={<ChartFallback />}>
                <PriceChart data={data.recentPrices} label={`${data.company.symbol} 30-day history`} />
              </Suspense>
              <Table>
                <TableCaption>Raw (unadjusted) daily prices leading up to the selected date. Source: Alpha Vantage.</TableCaption>
                <TableHeader>
                  <TableRow>
                    <TableHead scope="col">Date</TableHead>
                    <TableHead scope="col" className="text-right">Open</TableHead>
                    <TableHead scope="col" className="text-right">High</TableHead>
                    <TableHead scope="col" className="text-right">Low</TableHead>
                    <TableHead scope="col" className="text-right">Close</TableHead>
                    <TableHead scope="col" className="text-right">Volume</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {data.recentPrices.map((p) => (
                    <TableRow key={p.date}>
                      <TableCell className="font-medium">{fmtDate(p.date)}</TableCell>
                      <TableCell className="text-right font-mono tabular">{fmtMoney(p.open)}</TableCell>
                      <TableCell className="text-right font-mono tabular">{fmtMoney(p.high)}</TableCell>
                      <TableCell className="text-right font-mono tabular">{fmtMoney(p.low)}</TableCell>
                      <TableCell className="text-right font-mono tabular">{fmtMoney(p.close)}</TableCell>
                      <TableCell className="text-right font-mono tabular">{fmtVolume(p.volume)}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        ) : (
          <EmptySection
            title="No market data for this date"
            body={`Historical market data is not available for ${data.company.symbol} on ${fmtDate(data.snapshotDate)}. Try a nearby date.`}
          />
        )}
      </section>

      {/* Evidence */}
      <section aria-label="Historical evidence" data-tour="evidence" className="space-y-4">
        <h2 className="text-lg font-semibold">Historical evidence</h2>
        <p className="text-xs text-fg-dim">
          Every item carries its source and the date it became available. Regulatory evidence
          is eligible by filing date, never the period covered. News comes from{' '}
          {newsSourceLabel(currentSource)} only — sources are never mixed or substituted.
        </p>
        <EvidenceStream
          filings={data.filings}
          disclosures={data.corporateDisclosures}
          news={data.news}
          newsSource={currentSource}
        />
      </section>

      {/* Boundary */}
      <div className="flex items-center gap-4" aria-hidden="true">
        <Separator className="flex-1" />
        <span className="text-xs font-medium tracking-widest text-fg-dim uppercase">
          Historical knowledge ends here
        </span>
        <Separator className="flex-1" />
      </div>

      {/* Reveal */}
      <section aria-label={`What happened after ${fmtDate(data.snapshotDate)}`} data-tour="reveal" className="space-y-4">
        <h2 className="text-lg font-semibold">What Happened After {fmtDate(data.snapshotDate)}</h2>
        <p className="text-sm text-fg-muted">
          Post-cutoff reality — kept strictly separate from what was knowable then. You now know
          what followed that moment.
        </p>
        <Card className="border-temporal">
          <CardContent className="space-y-4 pt-6">
            {data.outcome.prices.length > 0 ? (
              <>
                <div className="flex flex-wrap items-center gap-x-6 gap-y-2">
                  <div className="flex items-center gap-2">
                    <TrendIcon value={outcomeChange} />
                    <span className="font-mono text-2xl font-semibold tabular">{fmtPct(outcomeChange)}</span>
                    <span className="text-xs text-fg-dim">30-day movement after {fmtDateShort(data.snapshotDate)}</span>
                  </div>
                </div>
                <Suspense fallback={<ChartFallback />}>
                  <PriceChart
                    data={[...data.outcome.prices].reverse()}
                    label={`${data.company.symbol} 30 days after`}
                    color="#c14a09"
                    markers={outcomeMarkers}
                  />
                </Suspense>
                {outcomeMarkers.length > 0 && (
                  <p className="text-xs text-fg-dim">
                    Vermilion dots mark post-cutoff SEC filings
                    {simExit ? ' and your simulation exit' : ''} on the timeline.
                  </p>
                )}
              </>
            ) : (
              <EmptySection
                title="No subsequent prices available"
                body="Subsequent price data is unavailable for this investigation."
              />
            )}

            {data.outcome.filings.length > 0 && (
              <div className="space-y-2">
                <h3 className="text-sm font-medium">SEC filings after the cutoff</h3>
                <ul className="space-y-2">
                  {data.outcome.filings.map((f) => (
                    <li key={f.accessionNumber} className="flex flex-wrap items-center gap-2 rounded-lg border border-border p-3 text-sm">
                      <Badge variant="secondary" className="font-mono">{f.formType}</Badge>
                      <span className="text-fg-muted">Filed {fmtDate(f.filedAt)}</span>
                      <a
                        href={f.url}
                        target="_blank"
                        rel="noreferrer"
                        className="ml-auto inline-flex items-center gap-1 text-primary underline-offset-4 hover:underline"
                      >
                        SEC.gov <ExternalLink className="size-3" aria-hidden="true" />
                      </a>
                    </li>
                  ))}
                </ul>
              </div>
            )}

            {data.outcome.liveQuote ? (
              <div className="flex flex-wrap items-center gap-x-4 gap-y-1 rounded-lg bg-accent p-3 text-sm">
                <span className="font-medium">Live context (delayed, {data.outcome.liveQuote.source})</span>
                <span className="font-mono tabular">{fmtMoney(data.outcome.liveQuote.currentPrice)}</span>
                <span className="flex items-center gap-1 font-mono tabular">
                  <TrendIcon value={data.outcome.liveQuote.percentChange} />
                  {fmtPct(data.outcome.liveQuote.percentChange)}
                </span>
                <span className="text-xs text-fg-dim">as of {fmtDateTimeUtc(data.outcome.liveQuote.asOfUtc)}</span>
              </div>
            ) : (
              <p className="text-xs text-fg-dim">Live quote currently unavailable.</p>
            )}
          </CardContent>
        </Card>
      </section>

      {hasMarket && close !== null && close !== 0 && (
        <Simulation symbol={data.company.symbol} entryDate={data.snapshotDate} entryClose={close} onExit={setSimExit} />
      )}
    </div>
  );
}

function useSnapshotStream(symbol: string, date: string, newsSource: NewsSource, nonce: number) {
  const [stages, setStages] = useState<StageEvent[]>([]);
  const [data, setData] = useState<SnapshotResponse | null>(null);
  const [error, setError] = useState<unknown>(null);

  useEffect(() => {
    if (symbol === '' || date === '') return;
    setStages([]);
    setData(null);
    setError(null);

    const url =
      `${API_BASE}/api/timemachine/snapshot/stream?symbol=${encodeURIComponent(symbol)}` +
      `&date=${encodeURIComponent(date)}&newsSource=${encodeURIComponent(newsSource)}`;
    const es = new EventSource(url);
    let settled = false;

    const onStage = (e: Event) => {
      try {
        const s = JSON.parse((e as MessageEvent).data) as StageEvent;
        setStages((prev) => [...prev.filter((p) => p.stage !== s.stage), s]);
      } catch {
        /* ignore malformed stage frames */
      }
    };
    const onSnapshot = (e: Event) => {
      try {
        setData(JSON.parse((e as MessageEvent).data) as SnapshotResponse);
        settled = true;
      } catch {
        setError(new Error('The investigation response could not be read.'));
      } finally {
        es.close();
      }
    };
    const onError = (e: Event) => {
      // Named server "error" events arrive as MessageEvents with payload;
      // connection failures arrive as plain Events.
      if (e instanceof MessageEvent && e.data) {
        try {
          const problem = JSON.parse(e.data) as { detail?: string };
          setError(new ApiError(problem.detail ?? 'Request failed', 500, problem));
        } catch {
          setError(new Error('Request failed'));
        }
        es.close();
      } else if (!settled) {
        setError(new ApiError('The investigation service is unreachable. Check that the backend is running and try again.', 0, null));
        es.close();
      }
    };

    es.addEventListener('stage', onStage);
    es.addEventListener('snapshot', onSnapshot);
    es.addEventListener('error', onError);
    return () => {
      settled = true;
      es.close();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [symbol, date, newsSource, nonce]);

  return { stages, data, error };
}

export default function Snapshot() {
  const [params] = useSearchParams();
  const symbol = params.get('symbol')?.trim() ?? '';
  const date = params.get('date')?.trim() ?? '';
  const newsSource = normalizeSource(params.get('newsSource'));
  const [nonce, setNonce] = useState(0);
  const { stages, data, error } = useSnapshotStream(symbol, date, newsSource, nonce);

  useEffect(() => {
    document.title = symbol && date
      ? `${symbol.toUpperCase()} · ${fmtDate(date)} — Stock Time Machine`
      : 'Stock Time Machine';
    if (symbol && date) recordInvestigation(symbol, date, newsSource);
  }, [symbol, date, newsSource]);

  if (symbol === '' || date === '') {
    return (
      <EmptySection
        title="No investigation selected"
        body="Choose a company and a historical date to reconstruct what was knowable then."
        action={
          <Link to="/investigate" className={buttonVariants()}>
            Start an investigation
          </Link>
        }
      />
    );
  }

  if (error && !data) {
    return (
      <ErrorState
        error={error}
        fallback="The investigation could not be completed."
        onRetry={() => setNonce((n) => n + 1)}
        backTo="/investigate"
      />
    );
  }

  if (!data) {
    return (
      <div className="space-y-4">
        <ReconstructionProgress stages={stages} />
        <LoadingDossier />
      </div>
    );
  }

  return <Dossier data={data} />;
}
