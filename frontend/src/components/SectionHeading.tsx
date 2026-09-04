import type { ReactNode } from 'react';

/**
 * Temporal section heading: a small caps kicker anchoring the section in the
 * journey's time model (before / after the cutoff, decide) above the title.
 * The kicker is the hierarchy device — titles stay calm.
 */
export function SectionHeading({ kicker, children }: { kicker: string; children: ReactNode }) {
  return (
    <div>
      <p className="text-[11px] font-semibold tracking-[0.14em] text-temporal uppercase">{kicker}</p>
      <h2 className="font-display text-xl font-semibold tracking-tight">{children}</h2>
    </div>
  );
}
