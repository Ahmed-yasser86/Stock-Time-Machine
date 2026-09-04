import { CartesianGrid, Line, LineChart, ReferenceArea, ReferenceDot, ReferenceLine, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { fmtDate, fmtDateShort, fmtMoney } from '../lib/format';
import type { KeyMove, PricePoint } from '../types';

/**
 * 100-day investigation window: continuous close line, numbered key-move
 * markers, and the decision-point rule. Markers are visual; the ranked list
 * below is the operable surface (keyboard + screen-reader friendly).
 */
export function MovesTimeline({
  prices,
  moves,
  decisionDate,
  selectedDate,
  onSelect,
  regimes = {},
}: {
  prices: PricePoint[];
  moves: KeyMove[];
  decisionDate: string;
  selectedDate: string | null;
  onSelect: (date: string) => void;
  regimes?: Record<string, string>;
}) {
  const asc = [...prices].sort((a, b) => (a.date < b.date ? -1 : 1));
  // Contiguous tense stretches become background washes; calm/normal/warming
  // stay unshaded. Regimes describe realized volatility — nothing predictive.
  const tenseRuns: { x1: string; x2: string }[] = [];
  {
    let runStart: string | null = null;
    let prev: string | null = null;
    for (const p of asc) {
      if (regimes[p.date] === 'tense') {
        if (runStart === null) runStart = p.date;
        prev = p.date;
      } else if (runStart !== null && prev !== null) {
        tenseRuns.push({ x1: runStart, x2: prev });
        runStart = null;
        prev = null;
      }
    }
    if (runStart !== null && prev !== null) tenseRuns.push({ x1: runStart, x2: prev });
  }
  const values = asc.map((p) => p.close).filter((v) => Number.isFinite(v));
  const min = values.length > 0 ? Math.min(...values) : 0;
  const max = values.length > 0 ? Math.max(...values) : 0;
  const pad = (max - min) * 0.1 || 1;
  const closeByDate = new Map(asc.map((p) => [p.date, p.close]));
  const rankByDate = new Map(moves.map((m, i) => [m.date, i + 1]));

  return (
    <div>
      <div
        role="img"
        aria-label={`100 trading days ending ${fmtDate(decisionDate)} with ${moves.length} key movements marked`}
      >
        <ResponsiveContainer width="100%" height={240}>
          <LineChart data={asc} margin={{ top: 12, right: 8, bottom: 0, left: 0 }}>
            <CartesianGrid stroke="#e8e1d1" strokeDasharray="3 3" vertical={false} />
            <XAxis
              dataKey="date"
              tickFormatter={(d) => fmtDateShort(typeof d === 'string' || typeof d === 'number' ? String(d) : '')}
              tick={{ fill: '#646c7c', fontSize: 11 }}
              axisLine={{ stroke: '#ddd3bf' }}
              tickLine={false}
              minTickGap={40}
            />
            <YAxis
              domain={[min - pad, max + pad]}
              tickFormatter={(v: number) => `$${Math.round(v)}`}
              tick={{ fill: '#646c7c', fontSize: 11 }}
              axisLine={false}
              tickLine={false}
              width={48}
            />
            <Tooltip
              contentStyle={{ background: '#ffffff', border: '1px solid #ddd3bf', borderRadius: 8, fontSize: 12, color: '#1a1f2b' }}
              labelFormatter={(d) => fmtDate(typeof d === 'string' || typeof d === 'number' ? String(d) : '')}
              formatter={(value) => [fmtMoney(value as number), 'Close']}
            />
            <Line type="monotone" dataKey="close" stroke="#0c5b66" strokeWidth={2} dot={false} />
            {tenseRuns.map((r) => (
              <ReferenceArea
                key={`${r.x1}-${r.x2}`}
                x1={r.x1}
                x2={r.x2}
                fill="#b42318"
                fillOpacity={0.07}
                stroke="none"
              />
            ))}
            {moves.map((m, i) =>
              closeByDate.has(m.date) ? (
                <ReferenceDot
              key={m.date}
              x={m.date}
              y={closeByDate.get(m.date) ?? 0}
                  r={selectedDate === m.date ? 7 : 5}
                  fill="#c14a09"
                  stroke="#ffffff"
                  strokeWidth={2}
                  label={{ value: String(i + 1), position: 'top', fill: '#c14a09', fontSize: 11, fontWeight: 700 }}
                />
              ) : null,
            )}
            <ReferenceLine
              x={decisionDate}
              stroke="#c14a09"
              strokeDasharray="4 3"
              label={{ value: 'decision', position: 'insideTopRight', fill: '#c14a09', fontSize: 11 }}
            />
          </LineChart>
        </ResponsiveContainer>
      </div>
      <div className="mt-2 flex flex-wrap gap-2" role="group" aria-label="Key movements, ranked">
        {moves.map((m, i) => (
          <button
            key={m.date}
            type="button"
            onClick={() => onSelect(m.date)}
            aria-pressed={selectedDate === m.date}
            className={`rounded-full border px-3 py-1 font-mono text-sm tabular transition-colors ${
              selectedDate === m.date
                ? 'border-temporal bg-temporal text-white'
                : 'border-border bg-surface text-fg hover:border-temporal'
            }`}
          >
            #{i + 1} {fmtDateShort(m.date)} {m.dailyReturnPct > 0 ? '+' : ''}{m.dailyReturnPct.toFixed(2)}%
          </button>
        ))}
      </div>
      <p className="mt-1 text-xs text-fg-dim">
        Ranked by significance score (see Methodology). Vermilion rule marks your decision date;
        everything right of it would be hindsight. Shaded stretches were tense volatility regimes
        (trailing volatility, window-relative tertiles — descriptive, not predictive). Rank {moves.length > 0 ? `1–${moves.length}` : '—'} below unlocks each move's evidence.
      </p>
      <span className="sr-only">Move ranks: {Array.from(rankByDate.entries()).map(([d, r]) => `${r} on ${d}`).join(', ')}</span>
    </div>
  );
}
