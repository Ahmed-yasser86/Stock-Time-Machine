import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { API_BASE, ApiError, api } from '../lib/api';
import { fmtPct } from '../lib/format';
import type { ClusterBrief, HypeSignalsResponse, NewsSource } from '../types';
import { Alert, AlertDescription, AlertTitle } from '../components/ui/alert';
import { Badge } from '../components/ui/badge';
import { Button } from '../components/ui/button';
import { Card, CardHeader, CardTitle } from '../components/ui/card';
import { Input } from '../components/ui/input';
import { Label } from '../components/ui/label';
import { CutoffRule } from '../components/CutoffRule';
import { GuidedTour } from '../components/GuidedTour';
import { EmptySection, ErrorState, ReconstructionProgress, type StageEvent } from '../components/StateBlocks';
import { MethodLink } from '../components/MethodLink';
import { SignalCard } from '../components/SignalCard';

const HYPE_STAGES = [
  { key: 'detecting', label: 'Detecting key movements' },
  { key: 'evidence', label: 'Attaching evidence to each move' },
  { key: 'embedding', label: 'Embedding articles for grouping' },
  { key: 'clustering', label: 'Clustering narrative threads' },
  { key: 'briefing', label: 'Writing AI briefs for the largest threads' },
  { key: 'projecting', label: 'Freezing peaks into hype cases' },
  { key: 'matching', label: 'Evaluating signal triggers' },
  { key: 'resembling', label: 'Joining resembling past peaks' },
];

/**
 * Inline hype stream (mirrors the moves inline stream, without persisted
 * jobs): stage events narrate detecting → threads → projecting → matching →
 * resembling, then the full signals payload. Disconnecting cancels via
 * RequestAborted; an explicit retry starts fresh.
 */
function useHypeStream(symbol: string, date: string, newsSource: NewsSource, nonce: number) {
  const [stages, setStages] = useState<StageEvent[]>([]);
  const [data, setData] = useState<HypeSignalsResponse | null>(null);
  const [error, setError] = useState<unknown>(null);

  useEffect(() => {
    if (symbol === '' || date === '') return;
    setStages([]);
    setData(null);
    setError(null);

    let cancelled = false;
    let settled = false;
    let es: EventSource | null = null;
    const onStage = (e: Event) => {
      try {
        const s = JSON.parse((e as MessageEvent).data) as StageEvent;
        setStages((prev) => [...prev.filter((p) => p.stage !== s.stage), s]);
      } catch {
        /* ignore malformed stage frames */
      }
    };
    const onSignals = (e: Event) => {
      try {
        setData(JSON.parse((e as MessageEvent).data) as HypeSignalsResponse);
        settled = true;
      } catch {
        if (!cancelled) setError(new Error('The hype response could not be read.'));
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
      `${API_BASE}/api/timemachine/hype/signals/stream?symbol=${encodeURIComponent(symbol)}&date=${encodeURIComponent(date)}&newsSource=${encodeURIComponent(newsSource)}`,
    );
    es.addEventListener('stage', onStage);
    es.addEventListener('signals', onSignals);
    es.addEventListener('error', onError);

    return () => {
      cancelled = true;
      settled = true;
      if (es) es.close();
    };
  }, [symbol, date, newsSource, nonce]);

  return { stages, data, error };
}

/**
 * Hype-cycle signals: deterministic pre-peak patterns (flags, thread
 * categories, regimes, sentiment) matched against the case library.
 * Co-occurrence is never presented as causation; briefs are AI-labeled;
 * aftermath stays collapsed behind click with a non-predictive disclaimer.
 */
export default function Hype() {
  const [params, setParams] = useSearchParams();
  const symbol = (params.get('symbol') ?? '').toUpperCase();
  const date = params.get('date') ?? '';
  const newsSource = (params.get('newsSource') ?? 'gdelt') as NewsSource;

  const [symbolInput, setSymbolInput] = useState(symbol);
  const [dateInput, setDateInput] = useState(date);
  const [sourceInput, setSourceInput] = useState<NewsSource>(newsSource);

  const ready = symbol.length > 0 && date.length > 0;
  const [nonce, setNonce] = useState(0);
  const stream = useHypeStream(symbol, date, newsSource, nonce);
  // Shape kept query-like so the results section below reads unchanged.
  const query = {
    data: stream.data ?? undefined,
    isPending: !stream.data && !stream.error,
    isError: !!stream.error && !stream.data,
    error: stream.error,
    refetch: () => setNonce((n) => n + 1),
  };

  // Opt-in analyst summaries (Step 5): one generation call per click, never
  // auto-fetched. Keyed by peak + signal; failures stay silent-but-honest
  // (button returns, no brief attached).
  const [briefs, setBriefs] = useState<Record<string, { brief?: ClusterBrief; busy: boolean }>>({});
  const requestBrief = (peakDate: string, signalId: string) => {
    const key = `${peakDate}|${signalId}`;
    if (briefs[key]?.busy || briefs[key]?.brief) return;
    setBriefs((prev) => ({ ...prev, [key]: { busy: true } }));
    api
      // Frozen-row brief path (Issue 4): the registry id for this window's
      // own peak — readable rows brief frozen evidence, otherwise live.
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
    if (!symbolInput.trim() || !dateInput) return;
    setParams({
      symbol: symbolInput.trim().toUpperCase(),
      date: dateInput,
      newsSource: sourceInput,
    });
  };

  return (
    <div className="space-y-8" data-tour="signals">
      <GuidedTour page="/hype" />
      <section aria-labelledby="hype-title" className="space-y-3">
        <div className="flex flex-wrap items-center gap-2">
          <h1 id="hype-title" className="font-display text-3xl font-semibold tracking-tight">
            Hype signals
          </h1>
          <MethodLink anchor="hype-signals" />
        </div>
        <p className="text-lg text-fg-muted">
          Repeating pre-peak patterns — detected deterministically, evidenced concretely.
        </p>
        <div className="flex flex-wrap items-end gap-3">
          <div className="space-y-1">
            <Label htmlFor="hype-symbol">Symbol</Label>
            <Input
              id="hype-symbol"
              value={symbolInput}
              onChange={(e) => setSymbolInput(e.target.value.toUpperCase())}
              placeholder="NFLX"
              className="w-28 font-mono"
            />
          </div>
          <div className="space-y-1">
            <Label htmlFor="hype-date">As of date</Label>
            <Input
              id="hype-date"
              type="date"
              value={dateInput}
              onChange={(e) => setDateInput(e.target.value)}
            />
          </div>
          <div className="space-y-1">
            <Label htmlFor="hype-source">News source</Label>
            <select
              id="hype-source"
              value={sourceInput}
              onChange={(e) => setSourceInput(e.target.value as NewsSource)}
              className="rounded-md border border-border bg-surface px-3 py-2 text-sm"
            >
              <option value="gdelt">GDELT</option>
              <option value="alphavantage">Alpha Vantage</option>
              <option value="marketaux">MarketAux</option>
            </select>
          </div>
          <Button onClick={run}>Detect signals</Button>
        </div>
      </section>

      {!ready && (
        <EmptySection
          title="Pick a symbol and date"
          body="Hype signals are detected over the 100 trading days ending at your date, using key moves and their pre-peak narratives."
        />
      )}

      {ready && query.isPending && (
        <div className="space-y-4" aria-busy="true">
          <p className="text-sm text-fg-muted">
            Detecting pre-peak patterns for {symbol}…
          </p>
          <ReconstructionProgress
            stages={stream.stages}
            defs={HYPE_STAGES}
            title="Detecting movements, clustering threads, matching the library — live."
            footnote="Every row is a real pipeline step: deterministic detection, per-move evidence, embeddings with live counts, case freezing, trigger evaluation, then resemblance joins."
          />
        </div>
      )}

      {ready && query.isError && (
        <ErrorState
          error={query.error}
          fallback="Hype signals could not be loaded."
          onRetry={() => query.refetch()}
        />
      )}

      {ready && query.data && (
        <div className="space-y-8">
          <CutoffRule date={query.data.asOfDate} label="Lens ends here — signal detection moment" />
          <p className="text-xs text-fg-dim">
            {query.data.casesConsidered} registry case(s) searched for prior occurrences.
            News via {query.data.newsSource}.
          </p>
          {query.data.peaks.length === 0 && (
            <EmptySection
              title="No key moves in this window"
              body="Without detected peaks there is nothing to match signals against. Try a nearby date."
            />
          )}
          {query.data.peaks.map((peak) => (
            <section key={peak.peakDate} aria-label={`Peak ${peak.peakDate}`} className="space-y-3">
              <Card>
                <CardHeader>
                  <div className="flex flex-wrap items-center gap-2">
                    <CardTitle className="text-base">
                      Peak {peak.peakDate} · {fmtPct(peak.dailyReturnPct)}
                    </CardTitle>
                    {peak.flags.map((f) => (
                      <Badge key={f} variant="secondary" className="font-mono">{f}</Badge>
                    ))}
                    {peak.completeness !== 'full' && (
                      <Badge variant="outline">incomplete case data ({peak.completeness})</Badge>
                    )}
                  </div>
                  <p className="text-xs text-fg-dim">
                    Detection score {peak.score.toFixed(3)} · signals below co-occurred with this
                    peak — proximity in time is never presented as causation.
                  </p>
                  {peak.recomputedNote && (
                    <p className="text-xs text-fg-dim italic">{peak.recomputedNote}</p>
                  )}
                </CardHeader>
              </Card>
              {peak.signals.length === 0 ? (
                <p className="text-sm text-fg-muted">
                  No known pre-peak pattern fired for this peak.
                </p>
              ) : (
                peak.signals.map((s) => {
                  const key = `${peak.peakDate}|${s.id}`;
                  const override = briefs[key];
                  return (
                    <SignalCard
                      key={s.id}
                      signal={override?.brief ? { ...s, brief: override.brief } : s}
                      newsSource={newsSource}
                      onBrief={() => requestBrief(peak.peakDate, s.id)}
                      briefBusy={override?.busy}
                    />
                  );
                })
              )}
            </section>
          ))}
          <Alert>
            <AlertTitle className="text-xs">Reading guide</AlertTitle>
            <AlertDescription className="text-xs">
              Signals describe what the pre-peak coverage looked like in past cases that
              resembled this one. They predict nothing and recommend nothing. Supporting
              cases link to their full moves investigations so every claim can be checked.
              See <Link to="/methodology#hype-signals" className="underline">methodology</Link>.
            </AlertDescription>
          </Alert>
        </div>
      )}
    </div>
  );
}
