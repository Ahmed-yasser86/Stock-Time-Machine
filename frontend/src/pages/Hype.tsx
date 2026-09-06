import { useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { api } from '../lib/api';
import { fmtPct } from '../lib/format';
import type { ClusterBrief, NewsSource } from '../types';
import { Alert, AlertDescription, AlertTitle } from '../components/ui/alert';
import { Badge } from '../components/ui/badge';
import { Button } from '../components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '../components/ui/card';
import { Input } from '../components/ui/input';
import { Label } from '../components/ui/label';
import { CutoffRule } from '../components/CutoffRule';
import { GuidedTour } from '../components/GuidedTour';
import { EmptySection, ErrorState } from '../components/StateBlocks';
import { MethodLink } from '../components/MethodLink';
import { SignalCard } from '../components/SignalCard';

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
  const query = useQuery({
    queryKey: ['hype-signals', symbol, date, newsSource],
    queryFn: () => api.hypeSignals(symbol, date, newsSource),
    enabled: ready,
    staleTime: 5 * 60_000,
    retry: 1,
  });

  // Opt-in analyst summaries (Step 5): one generation call per click, never
  // auto-fetched. Keyed by peak + signal; failures stay silent-but-honest
  // (button returns, no brief attached).
  const [briefs, setBriefs] = useState<Record<string, { brief?: ClusterBrief; busy: boolean }>>({});
  const requestBrief = (peakDate: string, signalId: string) => {
    const key = `${peakDate}|${signalId}`;
    if (briefs[key]?.busy || briefs[key]?.brief) return;
    setBriefs((prev) => ({ ...prev, [key]: { busy: true } }));
    api
      .hypeBrief({ symbol, date, newsSource, peakDate, signalId })
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
        <Card aria-busy="true" aria-label="Loading hype signals">
          <CardContent className="pt-6 text-sm text-fg-muted">
            Detecting pre-peak patterns — moves, threads, then library matching…
          </CardContent>
        </Card>
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
