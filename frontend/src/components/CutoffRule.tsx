import { fmtDate } from '../lib/format';
import { Separator } from './ui/separator';

/**
 * The product's signature motif: a vermilion temporal rule marking exactly
 * where knowable history ends. Used at every boundary in the journey so the
 * cutoff is recognizable at a glance, not just readable.
 */
export function CutoffRule({ date, label = 'Historical knowledge ends here' }: { date: string; label?: string }) {
  return (
    <div className="flex items-center gap-4" role="separator" aria-label={`${label} — ${fmtDate(date)} 23:59 US/Eastern`}>
      <Separator className="flex-1 bg-temporal/60" />
      <span className="text-center">
        <span className="block text-xs font-semibold tracking-widest text-temporal uppercase">
          {label}
        </span>
        <span className="block font-mono text-[11px] text-fg-dim">{fmtDate(date)} · 23:59 US/Eastern</span>
      </span>
      <Separator className="flex-1 bg-temporal/60" />
    </div>
  );
}
