import { CartesianGrid, Line, LineChart, ReferenceLine, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { fmtDate, fmtDateShort, fmtMoney } from '../lib/format';
import type { MarketReaction } from '../types';

/**
 * Post-move price path: the recorded closes after the movement with the move
 * close as a dashed reference. Descriptive only — no abnormal-return claims
 * (no benchmark exists), no causality implied. The numeric list beside it
 * remains the screen-reader and precision source.
 */
export function ReactionSparkline({
  reaction,
  moveClose,
  moveDate,
}: {
  reaction: MarketReaction[];
  moveClose: number;
  moveDate: string;
}) {
  if (reaction.length === 0) return null;
  const values = reaction.map((r) => r.close).filter((v) => Number.isFinite(v));
  const min = Math.min(moveClose, ...values);
  const max = Math.max(moveClose, ...values);
  const pad = (max - min) * 0.15 || 1;

  return (
    <div
      role="img"
      aria-label={`Closes after ${fmtDate(moveDate)} starting at ${fmtMoney(moveClose)}, ${reaction.length} trading days shown`}
    >
      <ResponsiveContainer width="100%" height={120}>
        <LineChart data={reaction} margin={{ top: 4, right: 4, bottom: 0, left: 0 }}>
          <CartesianGrid stroke="#e8e1d1" strokeDasharray="3 3" vertical={false} />
          <XAxis
            dataKey="date"
            tickFormatter={(d) => fmtDateShort(typeof d === 'string' || typeof d === 'number' ? String(d) : '')}
            tick={{ fill: '#646c7c', fontSize: 10 }}
            axisLine={{ stroke: '#ddd3bf' }}
            tickLine={false}
            minTickGap={28}
          />
          <YAxis
            domain={[min - pad, max + pad]}
            tickFormatter={(v: number) => `$${Math.round(v)}`}
            tick={{ fill: '#646c7c', fontSize: 10 }}
            axisLine={false}
            tickLine={false}
            width={40}
          />
          <Tooltip
            contentStyle={{ background: '#ffffff', border: '1px solid #ddd3bf', borderRadius: 8, fontSize: 12, color: '#1a1f2b' }}
            labelFormatter={(d) => fmtDate(typeof d === 'string' || typeof d === 'number' ? String(d) : '')}
            formatter={(value) => [fmtMoney(value as number), 'Close']}
          />
          <ReferenceLine y={moveClose} stroke="#646c7c" strokeDasharray="4 3" />
          <Line type="monotone" dataKey="close" stroke="#0c5b66" strokeWidth={2} dot={{ r: 2.5, fill: '#0c5b66' }} />
        </LineChart>
      </ResponsiveContainer>
    </div>
  );
}
