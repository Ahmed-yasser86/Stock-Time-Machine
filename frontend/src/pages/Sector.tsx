import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { API_BASE, ApiError, api } from '../lib/api';
import { fmtPct } from '../lib/format';
import type { ClusterBrief, HypeSectorResponse, NewsSource } from '../types';
import { Alert, AlertDescription, AlertTitle } from '../components/ui/alert';
import { Badge } from '../components/ui/badge';
import { Button } from '../components/ui/button';
import { Card, CardHeader, CardTitle } from '../components/ui/card';
import { Input } from '../components/ui/input';
import { Label } from '../components/ui/label';
import { CutoffRule } from '../components/CutoffRule';
import { EmptySection, ErrorState, ReconstructionProgress, type StageEvent } from '../components/StateBlocks';
import { MethodLink } from '../components/MethodLink';
import { SignalCard } from '../components/SignalCard';

/**
 * Sector sweep (Issue 10): up to 8 symbols under one shared as-of cutoff,
 * each row evaluated independently by the same signals pipeline. Rows stand
 * alone — no pooled verdicts, no cross-symbol scores, no rankings. Stream
 * mirrors the hype stream: per-symbol stage events, `row` events as each
 * symbol resolves, then the full `sector` payload.
 */
function useSectorStream(symbols: string, date: string, newsSource: NewsSource, nonce: number) {
  const [stages, setStages] = useState<StageEvent[]>([]);
  const [data, setData] = useState<HypeSectorResponse | null>(null);
  const [error, setError] = useState<unknown>(null);

  useEffect(() => {
    if (symbols === '' || date === '') return;
    setStages([]);
    setData(null);
    setError(null);

    let cancelled = false;
    let settled = false;
    let es: EventSource | null = null;
    const onStage = (e: Event) => {
      try {
        const s = JSON.parse((e as MessageEvent).data) as StageEvent;
        setStages((prev) => [...prev.filter((p) => p.detail !== s.detail || p.stage !== s.stage), s]);
      } catch {
        /* ignore malformed stage frames */
      }
    };
    const onSector = (e: Event) => {
      try {
        setData(JSON.parse((e as MessageEvent).data) as HypeSectorResponse);
        settled = true;
      } catch {
        if (!cancelled) setError(new Error('The sector response could not be read.'));
      } finally {
        if (es) es.close();
      }
    };
    const onError = (e: Event) => {
      if (e instanceof MessageEvent && e.data) {
        try {
          const problem = JSON.parse(e.data) as { detail?: string };
          if (!cancelled) setError(new ApiError(problem.detail ?? 'Request failed', 500, problem));
        } catch {
          if (!cancelled) setError(new Error('Request failed'));
        }
        if (es) es.close();
      } else if (!settled) {
        if (!cancelled) setError(new ApiError('The investigation service is unreachable. Check that the backend is running and try again.', 0, null));
        if (es) es.close();
      }
    };

    es = new EventSource(
      `${API_BASE}/api/timemachine/hype/sector/stream?symbols=${encodeURIComponent(symbols)}&date=${encodeURIComponent(date)}&newsSource=${encodeURIComponent(newsSource)}`,
    );
    es.addEventListener('stage', onStage);
    es.addEventListener('sector', onSector);
    es.addEventListener('error', onError);

    return () => {
      cancelled = true;
      settled = true;
      if (es) es.close();
    };
  }, [symbols, date, newsSource, nonce]);

  return { stages, data, error };
}

export default function Sector() {
  const [params, setParams] = useSearchParams();
  const symbols = (params.get('symbols') ?? '').toUpperCase();
  const date = params.get('date') ?? '';
  const newsSource = (params.get('newsSource') ?? 'gdelt') as NewsSource;

  const [symbolsInput, setSymbolsInput] = useState(symbols);
  const [dateInput, setDateInput] = useState(date);
  const [sourceInput, setSourceInput] = useState<NewsSource>(newsSource);

  const ready = symbols.length > 0 && date.length > 0;
  const [nonce, setNonce] = useState(0);
  const stream = useSectorStream(symbols, date, newsSource, nonce);
  const query = {
    data: stream.data ?? undefined,
    isPending: !stream.data && !stream.error,
    isError: !!stream.error && !stream.data,
    error: stream.error,
    refetch: () => setNonce((n) => n + 1),
  };

  // Opt-in briefs, keyed by row + peak + signal (same contract as Hype page).
  const [briefs, setBriefs] = useState<Record<string, { brief?: ClusterBrief; busy: boolean }>>({});
  const requestBrief = (symbol: string, peakDate: string, signalId: string) => {
    const key = `${symbol}|${peakDate}|${signalId}`;
    if (briefs[key]?.busy || briefs[key]?.brief) return;
    setBriefs((prev) => ({ ...prev, [key]: { busy: true } }));
    api
      .hypeBrief({ symbol, date, newsSource, peakDate, signalId, caseId: `${symbol}:${peakDate}` })
      .then((r) => {
        setBriefs((prev) => ({
          ...prev,
          [key]: r.brief ? { brief: r.brief, busy: false } : { busy: false },
        }));
      })
      .catch(() => {
        setBriefs((prev) => ({ ...prev, [key]: { busy: false } }));
      });
  };

  const run = () => {
    if (!symbolsInput.trim() || !dateInput) return;
    setParams({
      symbols: symbolsInput.trim().toUpperCase(),
      date: dateInput,
      newsSource: sourceInput,
    });
  };

  return (
    <div className="space-y-8">
      <div className="space-y-2">
        <h1 className="text-2xl font-semibold">Sector sweep</h1>
        <p className="text-sm text-fg-dim">
          Up to 8 symbols under one shared cutoff — each row evaluated alone.
          No pooled verdicts, no cross-symbol scores. <MethodLink anchor="sector-view" />
        </p>
      </div>

      <Card>
        <CardHeader className="space-y-3">
          <div className="grid gap-3 sm:grid-cols-4">
            <div className="space-y-1 sm:col-span-2">
              <Label htmlFor="sector-symbols">Symbols (comma-separated, max 8)</Label>
              <Input
                id="sector-symbols"
                value={symbolsInput}
                onChange={(e) => setSymbolsInput(e.target.value.toUpperCase())}
                placeholder="NVDA, MSFT, AAPL"
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor="sector-date">As-of date</Label>
              <Input id="sector-date" type="date" value={dateInput} onChange={(e) => setDateInput(e.target.value)} />
            </div>
            <div className="space-y-1">
              <Label htmlFor="sector-source">News source</Label>
              <select
                id="sector-source"
                className="w-full rounded-md border border-border bg-bg px-3 py-2 text-sm"
                value={sourceInput}
                onChange={(e) => setSourceInput(e.target.value as NewsSource)}
              >
                <option value="gdelt">GDELT</option>
                <option value="alphavantage">Alpha Vantage</option>
                <option value="marketaux">MarketAux</option>
              </select>
            </div>
          </div>
          <div>
            <Button onClick={run} disabled={!symbolsInput.trim() || !dateInput}>
              Sweep sector
            </Button>
          </div>
        </CardHeader>
      </Card>

      {!ready && (
        <EmptySection
          title="No sweep yet"
          body="Enter up to 8 symbols and one as-of date, then sweep. Each symbol is evaluated alone under the shared cutoff."
        />
      )}

      {ready && query.isPending && (
        <ReconstructionProgress
          stages={stream.stages}
          title="Sweeping symbols — live."
          footnote="One full signals pipeline per symbol, sequentially. Per-symbol failures become error rows, never a failed sweep."
        />
      )}
      {ready && query.isError && (
        <ErrorState
          error={query.error}
          fallback="Sector sweep could not be loaded."
          onRetry={query.refetch}
        />
      )}

      {ready && query.data && (
        <div className="space-y-6">
          <CutoffRule date={query.data.asOfDate} label="Shared cutoff — every row below ends here" />
          <p className="text-xs text-fg-dim">
            {query.data.rows.length} symbol(s) swept independently. News via {query.data.newsSource}.
          </p>
          {query.data.rows.map((row) => (
            <section key={row.symbol} aria-label={`Sector row ${row.symbol}`} className="space-y-3">
              <Card>
                <CardHeader>
                  <div className="flex flex-wrap items-center gap-2">
                    <CardTitle className="text-base">
                      <Link
                        to={`/moves?symbol=${encodeURIComponent(row.symbol)}&date=${query.data!.asOfDate}&newsSource=${query.data!.newsSource}`}
                        className="underline decoration-dotted underline-offset-2 hover:text-fg"
                      >
                        {row.symbol}
                      </Link>
                    </CardTitle>
                    <span className="text-xs text-fg-dim">{row.company.name}</span>
                    {row.error ? (
                      <Badge variant="destructive">row failed</Badge>
                    ) : (
                      <Badge variant="secondary" className="font-mono">
                        {row.peaks.length} peak{row.peaks.length === 1 ? '' : 's'}
                      </Badge>
                    )}
                  </div>
                  {row.error && (
                    <Alert variant="destructive">
                      <AlertTitle>Could not evaluate {row.symbol}</AlertTitle>
                      <AlertDescription>{row.error} Other rows are unaffected.</AlertDescription>
                    </Alert>
                  )}
                </CardHeader>
              </Card>
              {!row.error &&
                row.peaks.map((peak) => (
                  <div key={peak.peakDate} className="space-y-3 border-l-2 border-border pl-4">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="font-mono text-sm">
                        {peak.peakDate} · {fmtPct(peak.dailyReturnPct)}
                      </span>
                      {peak.flags.map((f) => (
                        <Badge key={f} variant="secondary" className="font-mono text-[10px]">{f}</Badge>
                      ))}
                      {peak.completeness !== 'full' && (
                        <Badge variant="outline">incomplete case data ({peak.completeness})</Badge>
                      )}
                    </div>
                    {peak.recomputedNote && (
                      <p className="text-xs text-fg-dim italic">{peak.recomputedNote}</p>
                    )}
                    {peak.signals.length === 0 ? (
                      <p className="text-sm text-fg-muted">No known pre-peak pattern fired for this peak.</p>
                    ) : (
                      peak.signals.map((s) => {
                        const key = `${row.symbol}|${peak.peakDate}|${s.id}`;
                        const override = briefs[key];
                        return (
                          <SignalCard
                            key={s.id}
                            signal={override?.brief ? { ...s, brief: override.brief } : s}
                            newsSource={query.data!.newsSource}
                            onBrief={() => requestBrief(row.symbol, peak.peakDate, s.id)}
                            briefBusy={override?.busy}
                          />
                        );
                      })
                    )}
                  </div>
                ))}
            </section>
          ))}
        </div>
      )}
    </div>
  );
}
