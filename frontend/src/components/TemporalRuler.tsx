import { fmtDate, fmtMoney } from '../lib/format';
import type { PricePoint } from '../types';

interface RulerDay {
  date: string;
  close: number | null;
  volume: number | null;
  isSelected: boolean;
  isFuture: boolean; // after the investigation cutoff
}

/**
 * Temporal ruler: the investigation's time axis made manipulable.
 * Every trading day with data is a bar (height ∝ volume); the selected date
 * is marked in vermilion; post-cutoff days sit beyond the cutoff rule.
 * Selecting another day starts a new investigation at that date.
 */
export function TemporalRuler({
  history,
  outcome,
  selectedDate,
  onSelect,
}: {
  history: PricePoint[];
  outcome: PricePoint[];
  selectedDate: string;
  onSelect: (date: string) => void;
}) {
  const histAsc = [...history].reverse();
  const days: RulerDay[] = [];
  for (const p of histAsc) {
    days.push({ date: p.date, close: p.close, volume: p.volume, isSelected: p.date === selectedDate, isFuture: false });
  }
  if (!days.some((d) => d.date === selectedDate)) {
    // Selected date has no bar (weekend/holiday): insert a marker in order.
    const idx = days.findIndex((d) => d.date > selectedDate);
    const marker: RulerDay = { date: selectedDate, close: null, volume: null, isSelected: true, isFuture: false };
    if (idx < 0) days.push(marker);
    else days.splice(idx, 0, marker);
  }
  for (const p of outcome) {
    days.push({ date: p.date, close: p.close, volume: p.volume, isSelected: false, isFuture: true });
  }

  const maxVol = Math.max(1, ...days.map((d) => d.volume ?? 0));
  const cutoffIndex = days.findIndex((d) => d.isFuture);

  return (
    <div aria-label="Temporal ruler">
      <div className="flex items-end gap-[3px] overflow-x-auto pb-1" role="group" aria-label="Trading days around the investigation date">
        {days.map((d, i) => {
          const weekday = new Date(d.date + 'T12:00:00').getDay();
          const weekend = weekday === 0 || weekday === 6;
          return (
            <div key={d.date + (d.isFuture ? '-o' : '')} className="flex flex-col items-center gap-1">
              {i === cutoffIndex && (
                <span className="mb-1 border-l-2 border-dashed border-temporal px-1 text-[10px] font-medium tracking-wide text-temporal uppercase" aria-hidden="true">
                  cutoff
                </span>
              )}
              <button
                type="button"
                onClick={() => onSelect(d.date)}
                disabled={d.isFuture}
                title={d.close !== null ? `${fmtDate(d.date)} — close ${fmtMoney(d.close)}` : `${fmtDate(d.date)} — no trading data`}
                aria-label={d.close !== null ? `${fmtDate(d.date)}, close ${fmtMoney(d.close)}${d.isSelected ? ', selected' : ''}` : `${fmtDate(d.date)}, markets closed`}
                aria-current={d.isSelected ? 'date' : undefined}
                className={`flex h-16 w-7 flex-col justify-end rounded-sm border p-[3px] transition-colors ${
                  d.isSelected
                    ? 'border-temporal bg-temporal/10'
                    : weekend
                      ? 'border-border-subtle bg-bg-deep'
                      : 'border-border bg-surface hover:border-primary'
                } ${d.isFuture ? 'cursor-default opacity-70' : 'cursor-pointer'}`}
              >
                <span
                  aria-hidden="true"
                  style={{ height: `${d.volume !== null ? Math.max(8, Math.round((d.volume / maxVol) * 100)) : 4}%` }}
                  className={`w-full rounded-[2px] ${d.isSelected ? 'bg-temporal' : d.isFuture ? 'bg-fg-dim/60' : 'bg-primary/70'}`}
                />
              </button>
              <span className={`text-[10px] tabular ${d.isSelected ? 'font-semibold text-temporal' : 'text-fg-dim'}`} aria-hidden="true">
                {new Date(d.date + 'T12:00:00').getDate()}
              </span>
            </div>
          );
        })}
      </div>
      <p className="mt-1 text-xs text-fg-dim">
        Bar height ∝ volume. Vermilion marks your date; days past the cutoff rule are dimmed and not selectable.
      </p>
    </div>
  );
}
