import { CartesianGrid, Line, LineChart, ReferenceDot, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { fmtDateShort, fmtMoney } from '../lib/format';
import type { PricePoint } from '../types';

export interface ChartMarker {
  date: string;
  label: string;
}

/**
 * Single-question price chart: "how did the close move across these dates?"
 * Event markers (filings, simulation exit) are visual annotations only —
 * the same facts always exist as DOM lists nearby for screen readers.
 */
export function PriceChart({
  data,
  label,
  color = '#0c5b66',
  markers = [],
}: {
  data: PricePoint[];
  label: string;
  color?: string;
  markers?: ChartMarker[];
}) {
  const asc = [...data].reverse();
  const values = asc.map((p) => p.close).filter((v) => Number.isFinite(v));
  const min = values.length > 0 ? Math.min(...values) : 0;
  const max = values.length > 0 ? Math.max(...values) : 0;
  const pad = (max - min) * 0.1 || 1;
  const closeByDate = new Map(asc.map((p) => [p.date, p.close]));
  const plotted = markers.filter((m) => closeByDate.has(m.date));

  return (
    <div role="img" aria-label={`${label}: closing prices from ${asc[0]?.date ?? ''} to ${asc[asc.length - 1]?.date ?? ''}`}>
      <ResponsiveContainer width="100%" height={220}>
        <LineChart data={asc} margin={{ top: 12, right: 8, bottom: 0, left: 0 }}>
          <CartesianGrid stroke="#e8e1d1" strokeDasharray="3 3" vertical={false} />
          <XAxis
            dataKey="date"
            tickFormatter={(d) => fmtDateShort(typeof d === 'string' || typeof d === 'number' ? String(d) : '')}
            tick={{ fill: '#646c7c', fontSize: 11 }}
            axisLine={{ stroke: '#ddd3bf' }}
            tickLine={false}
            minTickGap={32}
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
            labelFormatter={(d) => fmtDateShort(typeof d === 'string' || typeof d === 'number' ? String(d) : '')}
            formatter={(value) => [fmtMoney(value as number), 'Close']}
          />
          <Line type="monotone" dataKey="close" stroke={color} strokeWidth={2} dot={false} />
          {plotted.map((m) => (
            <ReferenceDot
              key={`${m.date}-${m.label}`}
              x={m.date}
              y={closeByDate.get(m.date)}
              r={4}
              fill="#c14a09"
              stroke="#ffffff"
              strokeWidth={1.5}
            />
          ))}
        </LineChart>
      </ResponsiveContainer>
    </div>
  );
}
