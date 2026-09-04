import { useEffect } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { useQueries } from '@tanstack/react-query';
import {
  CartesianGrid,
  Legend,
  Line,
  LineChart,
  ReferenceLine,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts';
import { api } from '../lib/api';
import { fmtDate, fmtPct } from '../lib/format';
import { MAX_COMPARE_PICKS, pickColor } from '../lib/palette';
import type { MovesResponse, NewsSource } from '../types';
import { Alert, AlertDescription, AlertTitle } from '../components/ui/alert';
import { buttonVariants } from '../components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '../components/ui/card';
import { Input } from '../components/ui/input';
import { Label } from '../components/ui/label';
import { EmptySection, ErrorState } from '../components/StateBlocks';
import { SymbolPicker } from '../components/SymbolPicker';

function parseSymbols(raw: string | null): string[] {
  if (!raw) return [];
  return [...new Set(raw.split(',').map((s) => s.trim().toUpperCase()).filter(Boolean))].slice(
    0,
    MAX_COMPARE_PICKS,
  );
}

interface AlignedSeries {
  symbol: string;
  color: string;
  points: { date: string; value: number }[];
  droppedDays: number;
}

/** Index every pick to 100 on the first date ALL picks traded. Days missing
 * in any pick are dropped for every pick (disclosed, never interpolated). */
function align(responses: { symbol: string; data: MovesResponse }[]): {
  series: AlignedSeries[];
  commonDates: string[];
} {
  const byDate = new Map<string, Map<string, number>>();
  for (const { symbol, data } of responses) {
    for (const p of data.windowPrices) {
      if (!byDate.has(p.date)) byDate.set(p.date, new Map());
      byDate.get(p.date)!.set(symbol, p.close);
    }
  }
  const symbols = responses.map((r) => r.symbol);
  const commonDates = [...byDate.entries()]
    .filter(([, m]) => symbols.every((s) => m.has(s)))
    .map(([d]) => d)
    .sort();
  const series: AlignedSeries[] = responses.map(({ symbol, data }, i) => {
    const base = byDate.get(commonDates[0])?.get(symbol) ?? 0;
    return {
      symbol,
      color: pickColor(i),
      droppedDays: data.windowPrices.length - commonDates.length,
      points: commonDates.map((d) => ({
        date: d,
        value: base > 0 ? (byDate.get(d)!.get(symbol)! / base) * 100 : 0,
      })),
    };
  });
  return { series, commonDates };
}

export default function Compare() {
  const [params, setParams] = useSearchParams();
  const picks = parseSymbols(params.get('symbols'));
  const date = params.get('date')?.trim() ?? '';
  const newsSource = (params.get('newsSource') as NewsSource | null) ?? 'gdelt';

  const picksKey = picks.join('|');
  useEffect(() => {
    document.title =
      picks.length > 0 && date
        ? `${picks.join(' vs ')} · to ${fmtDate(date)} — Stock Time Machine`
        : 'Compare companies — Stock Time Machine';
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [picksKey, date]);

  const results = useQueries({
    queries: picks.map((symbol) => ({
      queryKey: ['moves', symbol, date, newsSource],
      queryFn: () => api.moves(symbol, date, newsSource),
      enabled: symbol !== '' && date !== '',
      staleTime: 5 * 60_000,
      retry: 1,
    })),
  });

  const setPicks = (next: string[]) => {
    const p: Record<string, string> = { date, newsSource };
    if (next.length > 0) p.symbols = next.join(',');
    setParams(p, { replace: true });
  };

  const ready = results
    .map((r, i) => ({ symbol: picks[i], r }))
    .filter((x) => x.r.isSuccess && x.r.data.summary.sufficientHistory)
    .map((x) => ({ symbol: x.symbol, data: x.r.data! }));
  const failed = results
    .map((r, i) => ({ symbol: picks[i], r }))
    .filter((x) => x.r.isError);
  const pending = results.some((r) => r.isPending);
  const thin = results
    .map((r, i) => ({ symbol: picks[i], r }))
    .filter((x) => x.r.isSuccess && !x.r.data.summary.sufficientHistory);

  const { series, commonDates } = ready.length >= 2 ? align(ready) : { series: [], commonDates: [] };
  const rows = ready.length >= 2 ? commonDates.map((d, i) => {
    const row: Record<string, string | number> = { date: d };
    for (const s of series) row[s.symbol] = Number(s.points[i].value.toFixed(2));
    return row;
  }) : [];

  return (
    <div className="space-y-6">
      <div className="space-y-2">
        <h1 className="font-display text-3xl font-semibold tracking-tight">Compare companies</h1>
        <p className="text-sm text-fg-muted">
          Relative performance on one normalized axis — every pick indexed to 100 on the first
          date all of them traded, all cut off at the same decision date. Not raw prices, never
          mixed timelines.
        </p>
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Picks &amp; decision date</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          <SymbolPicker picks={picks} onChange={setPicks} />
          <div className="max-w-xs space-y-1">
            <Label htmlFor="compare-date">Decision date (shared cutoff)</Label>
            <Input
              id="compare-date"
              type="date"
              value={date}
              max={new Date().toISOString().slice(0, 10)}
              onChange={(e) => setParams({ symbols: picks.join(','), date: e.target.value, newsSource }, { replace: true })}
            />
          </div>
        </CardContent>
      </Card>

      {picks.length === 0 || date === '' ? (
        <EmptySection
          title="Choose companies and a date"
          body={`Pick up to ${MAX_COMPARE_PICKS} companies and one shared decision date. Comparison starts from cached prices — no extra provider cost.`}
        />
      ) : (
        <>
          {pending && (
            <p className="text-sm text-fg-muted" aria-busy="true">
              Loading {picks.length} investigation{picks.length > 1 ? 's' : ''} — each pick reuses its cached 100-day window…
            </p>
          )}
          {failed.map(({ symbol, r }) => (
            <ErrorState
              key={symbol}
              error={r.error}
              fallback={`${symbol} could not be loaded — it is excluded, the rest still compare.`}
              onRetry={() => r.refetch()}
            />
          ))}
          {thin.map(({ symbol }) => (
            <Alert key={symbol}>
              <AlertTitle>{symbol}: insufficient history</AlertTitle>
              <AlertDescription>
                Fewer than 30 trading days before {fmtDate(date)} — excluded from the comparison rather than shown partially.
              </AlertDescription>
            </Alert>
          ))}

          {ready.length >= 2 && (
            <>
              <Card>
                <CardHeader>
                  <CardTitle className="text-base">Relative performance (indexed to 100)</CardTitle>
                  <p className="text-xs text-fg-dim">
                    {commonDates.length} shared trading days from {fmtDate(commonDates[0])} to{' '}
                    {fmtDate(commonDates[commonDates.length - 1])}. Days missing in any pick are
                    dropped for all picks — never interpolated.
                  </p>
                </CardHeader>
                <CardContent>
                  <div className="h-80" role="img" aria-label={`Indexed closes for ${picks.join(', ')}`}>
                    <ResponsiveContainer width="100%" height="100%">
                      <LineChart data={rows} margin={{ top: 8, right: 16, bottom: 8, left: 0 }}>
                        <CartesianGrid strokeDasharray="3 3" stroke="var(--color-border-subtle)" />
                        <XAxis dataKey="date" tickFormatter={(d: string) => fmtDate(d)} minTickGap={48} tick={{ fontSize: 11 }} />
                        <YAxis tick={{ fontSize: 11 }} domain={['auto', 'auto']} />
                        <Tooltip
                          labelFormatter={(d) => fmtDate(String(d))}
                          formatter={(v, name) => [`${Number(v).toFixed(2)}`, name]}
                        />
                        <Legend />
                        {series.map((s) => (
                          <Line
                            key={s.symbol}
                            type="monotone"
                            dataKey={s.symbol}
                            stroke={s.color}
                            strokeWidth={2}
                            dot={false}
                            name={s.symbol}
                          />
                        ))}
                        <ReferenceLine
                          x={commonDates[commonDates.length - 1]}
                          stroke="var(--color-temporal)"
                          strokeWidth={2}
                          label={{ value: 'decision', fill: 'var(--color-temporal)', fontSize: 11 }}
                        />
                      </LineChart>
                    </ResponsiveContainer>
                  </div>
                </CardContent>
              </Card>

              <Card>
                <CardHeader>
                  <CardTitle className="text-base">Window stats per pick</CardTitle>
                  <p className="text-xs text-fg-dim">Each pick's own 100-day summary (deterministic, same engine as Lens).</p>
                </CardHeader>
                <CardContent className="overflow-x-auto">
                  <table className="w-full text-sm">
                    <thead>
                      <tr className="border-b border-border text-left text-xs text-fg-dim">
                        <th className="py-2 pr-4 font-medium">Pick</th>
                        <th className="py-2 pr-4 font-medium">100-day return</th>
                        <th className="py-2 pr-4 font-medium">Volatility (ann.)</th>
                        <th className="py-2 pr-4 font-medium">Max drawdown</th>
                        <th className="py-2 pr-4 font-medium">Best day</th>
                        <th className="py-2 font-medium">Worst day</th>
                      </tr>
                    </thead>
                    <tbody>
                      {ready.map(({ symbol, data }, i) => (
                        <tr key={symbol} className="border-b border-border-subtle">
                          <td className="py-2 pr-4">
                            <span className="font-mono font-medium" style={{ borderLeft: `4px solid ${pickColor(i)}`, paddingLeft: 8 }}>
                              {symbol}
                            </span>
                          </td>
                          <td className="py-2 pr-4 font-mono tabular">
                            {data.summary.cumulativeReturnPct > 0 ? '+' : ''}
                            {data.summary.cumulativeReturnPct.toFixed(2)}%
                          </td>
                          <td className="py-2 pr-4 font-mono tabular">{data.summary.volatility.toFixed(2)}%</td>
                          <td className="py-2 pr-4 font-mono tabular">{fmtPct(data.summary.maxDrawdownPct)}</td>
                          <td className="py-2 pr-4 font-mono tabular">
                            {data.summary.bestDay ? `${fmtDate(data.summary.bestDay)} (${fmtPct(data.summary.bestDayReturnPct)})` : '—'}
                          </td>
                          <td className="py-2 font-mono tabular">
                            {data.summary.worstDay ? `${fmtDate(data.summary.worstDay)} (${fmtPct(data.summary.worstDayReturnPct)})` : '—'}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </CardContent>
              </Card>

              <div className="flex flex-wrap gap-2">
                {ready.map(({ symbol }) => (
                  <Link
                    key={symbol}
                    to={`/moves?symbol=${symbol}&date=${date}&newsSource=${newsSource}`}
                    className={buttonVariants({ variant: 'outline', size: 'sm' })}
                  >
                    Open {symbol} lens
                  </Link>
                ))}
              </div>
            </>
          )}

          {ready.length === 1 && !pending && (
            <Alert>
              <AlertTitle>Only one pick loaded</AlertTitle>
              <AlertDescription>
                Comparison needs at least two healthy picks. Add another company or fix the failed one above.
              </AlertDescription>
            </Alert>
          )}
        </>
      )}
    </div>
  );
}
