import { Link } from 'react-router-dom';
import type { HypeSignal, NewsSource } from '../types';
import { AiBriefBlock } from './AiBriefBlock';
import { Badge } from './ui/badge';
import { Button } from './ui/button';
import { Card, CardContent, CardHeader, CardTitle } from './ui/card';
import { useState } from 'react';

// Signed percent with explicit +/− and em-dash for null (never 0-filled:
// null means unmeasured, not flat).
function formatSignedPct(value: number | null | undefined): string {
  if (value === null || value === undefined) return '—';
  const sign = value > 0 ? '+' : '';
  return `${sign}${value.toFixed(2)}%`;
}

/**
 * One detected hype signal. Deterministic trigger + concrete evidence refs
 * always render; resemblance (Step 4), LLM brief (Step 5), and realized
 * aftermath (Step 6) render only when the API attaches them. Supporting
 * cases deep-link to their moves investigation. Incomplete supporting cases
 * are flagged, never hidden.
 */
export function SignalCard({
  signal,
  newsSource,
  onBrief,
  briefBusy,
}: {
  signal: HypeSignal;
  newsSource: NewsSource;
  onBrief?: () => void;
  briefBusy?: boolean;
}) {
  const [followedOpen, setFollowedOpen] = useState(false);

  return (
    <Card>
      <CardHeader>
        <div className="flex flex-wrap items-center gap-2">
          <CardTitle className="text-base">{signal.name}</CardTitle>
          <Badge variant="secondary" className="font-mono">{signal.id}</Badge>
        </div>
        <p className="text-xs text-fg-dim">{signal.trigger}</p>
      </CardHeader>
      <CardContent className="space-y-3">
        <div>
          <p className="text-xs font-medium text-fg-dim">Triggering evidence</p>
          <ul className="mt-1 list-disc space-y-1 pl-5 text-sm">
            {signal.triggerEvidence.map((e, i) => (
              <li key={i}>{e}</li>
            ))}
          </ul>
        </div>

        <div data-tour="supporting">
          <p className="text-xs font-medium text-fg-dim">
            Seen before in {signal.supportingCases.length} historical case(s)
          </p>
          {signal.supportingCases.length === 0 ? (
            <p className="mt-1 text-sm text-fg-muted">
              First occurrence in the case library — no prior cases fired this trigger.
            </p>
          ) : (
            <div className="mt-1 flex flex-wrap gap-2">
              {signal.supportingCases.map((c) => (
                <Link
                  key={c.id}
                  to={`/moves?symbol=${encodeURIComponent(c.symbol)}&date=${c.peakDate}&newsSource=${newsSource}`}
                  className="rounded-full border border-border px-3 py-1 font-mono text-xs hover:border-primary hover:text-fg"
                  title={`Open ${c.symbol} peak ${c.peakDate}`}
                >
                  {c.symbol} {c.peakDate}
                  {c.completeness !== 'full' && (
                    <span className="ml-1 text-fg-dim">({c.completeness} data)</span>
                  )}
                </Link>
              ))}
            </div>
          )}
        </div>

        {signal.resemblance && signal.resemblance.length > 0 && (
          <div>
            <p className="text-xs font-medium text-fg-dim">Resembles past peaks (embedding similarity)</p>
            <ul className="mt-1 space-y-1 text-sm">
              {signal.resemblance.map((r) => (
                <li key={r.caseId}>
                  <Badge variant="secondary" className="mr-1 font-mono text-[10px]">
                    {r.kind === 'strong' ? 'strong match' : r.kind === 'pattern' ? 'pattern match' : 'narrative match'}
                  </Badge>
                  <Link
                    to={`/moves?symbol=${encodeURIComponent(r.symbol)}&date=${r.peakDate}&newsSource=${newsSource}`}
                    className="font-mono underline decoration-dotted underline-offset-2 hover:text-fg"
                  >
                    {r.symbol} {r.peakDate}
                  </Link>{' '}
                  <span className="text-xs text-fg-dim">similarity {r.similarity.toFixed(2)} — resemblance, not relatedness proof</span>
                </li>
              ))}
            </ul>
          </div>
        )}

        {onBrief && !signal.brief && (
          <div>
            <Button
              variant="outline"
              size="sm"
              disabled={briefBusy}
              onClick={onBrief}
            >
              {briefBusy ? 'Writing analyst summary…' : 'Write analyst summary (AI)'}
            </Button>
          </div>
        )}
        {signal.brief && (
          <AiBriefBlock brief={signal.brief} context="analyst summary of this signal's pre-peak coverage" />
        )}

        {signal.followed && signal.followed.length > 0 && (
          <div data-tour="aftermath" className="space-y-2">
            <p className="text-xs text-fg-dim">
              Observed in past cases — never a forecast. Realized prices only; nothing here
              predicts this peak. This is not investment advice.
            </p>
            {signal.followedSummary && signal.followedSummary.casesWithReaction > 0 && (
              <p className="text-sm">
                Across {signal.followedSummary.casesWithReaction} past case(s): median 5-day
                move {formatSignedPct(signal.followedSummary.medianMovePct)} · observed high{' '}
                {formatSignedPct(signal.followedSummary.observedHighPct)} · observed low{' '}
                {formatSignedPct(signal.followedSummary.observedLowPct)}
              </p>
            )}
            <div>
              <Button
                variant="outline"
                size="sm"
                onClick={() => setFollowedOpen((v) => !v)}
                aria-expanded={followedOpen}
              >
                {followedOpen ? 'Hide what followed' : `Show what followed (${signal.followed.length} cases)`}
              </Button>
            </div>
            {followedOpen && (
              <ul className="space-y-1 text-sm">
                {signal.followed.map((f) => (
                  <li key={f.caseId} className="font-mono text-xs">
                    <Link
                      to={`/moves?symbol=${encodeURIComponent(f.symbol)}&date=${f.peakDate}&newsSource=${newsSource}`}
                      className="underline decoration-dotted underline-offset-2 hover:text-fg"
                      title={`Open the full ${f.symbol} ${f.peakDate} investigation`}
                    >
                      {f.symbol} {f.peakDate}
                    </Link>
                    : {f.reaction.map((r) => `${r.date} ${r.close}`).join(' · ') || 'no reaction data'}
                  </li>
                ))}
              </ul>
            )}
          </div>
        )}
      </CardContent>
    </Card>
  );
}
