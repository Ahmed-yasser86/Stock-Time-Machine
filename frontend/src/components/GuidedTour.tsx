import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Button } from './ui/button';
import { Card, CardContent } from './ui/card';

interface TourStep {
  target: string;
  title: string;
  body: string;
}

const STEPS: Record<string, TourStep[]> = {
  '/snapshot': [
    {
      target: '[data-tour="boundary"]',
      title: '1 · The boundary',
      body: 'Everything above the cutoff was knowable then; everything below it is what followed. That line is the whole product.',
    },
    {
      target: '[data-tour="evidence"]',
      title: '2 · The evidence',
      body: 'Filings, disclosures, and news with provenance on every item. Filter it, open primaries, trust nothing unsourced.',
    },
    {
      target: '[data-tour="reveal"]',
      title: '3 · The reveal',
      body: 'What actually followed — kept strictly separate from what was knowable. Then simulate, or open the 100-day lens.',
    },
  ],
  '/moves': [
    {
      target: '[data-tour="uncertainty"]',
      title: '1 · Uncertainty first',
      body: 'A transparent 0–100 gauge of how thin the evidence is. Read it before trusting anything below.',
    },
    {
      target: '[data-tour="timeline"]',
      title: '2 · The lens',
      body: 'Five ranked movements on the 100-day line. Select one to open its evidence drawer.',
    },
    {
      target: '[data-tour="threads"]',
      title: '3 · The threads',
      body: 'Narrative clusters with optional AI briefs — labels are vocabulary, briefs are generated. Verify against articles.',
    },
  ],
};

/**
 * First-run guided reveal (?guided=1): three anchored orientation cards per
 * page, dismissible, remembered per page in localStorage. Steps whose targets
 * are absent are skipped, never blocking.
 */
export function GuidedTour({ page }: { page: '/snapshot' | '/moves' }) {
  const [params, setParams] = useSearchParams();
  const [index, setIndex] = useState(0);
  const key = `stm:tour-seen:${page}`;
  const [seen, setSeen] = useState(() => {
    try {
      return localStorage.getItem(key) === '1';
    } catch {
      return true;
    }
  });

  const active = params.get('guided') === '1' && !seen;
  const steps = STEPS[page] ?? [];

  useEffect(() => {
    if (!active) return;
    const el = document.querySelector(steps[index]?.target ?? '');
    el?.scrollIntoView({ block: 'center', behavior: 'smooth' });
  }, [active, index, steps]);

  if (!active || steps.length === 0) return null;
  const step = steps[Math.min(index, steps.length - 1)];

  const dismiss = (remember: boolean) => {
    if (remember) {
      try {
        localStorage.setItem(key, '1');
      } catch {
        /* ignore */
      }
      setSeen(true);
    }
    const next = new URLSearchParams(params);
    next.delete('guided');
    setParams(next, { replace: true });
  };

  return (
    <Card
      role="dialog"
      aria-label={`Guided tour: ${step.title}`}
      className="no-print fixed bottom-4 left-4 right-4 z-50 border-temporal md:left-auto md:w-[24rem]"
    >
      <CardContent className="space-y-2 pt-4">
        <p className="text-sm font-medium">
          {step.title} <span className="text-xs font-normal text-fg-dim">({index + 1} of {steps.length})</span>
        </p>
        <p className="text-sm text-fg-muted">{step.body}</p>
        <div className="flex flex-wrap gap-2">
          {index < steps.length - 1 ? (
            <Button size="sm" onClick={() => setIndex(index + 1)}>
              Next
            </Button>
          ) : (
            <Button size="sm" onClick={() => dismiss(true)}>
              Got it
            </Button>
          )}
          <Button size="sm" variant="ghost" onClick={() => dismiss(false)}>
            Skip tour
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}
