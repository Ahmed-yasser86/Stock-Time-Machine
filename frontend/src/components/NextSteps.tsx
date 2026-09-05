import { useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../lib/api';
import type { ClusterBrief, MovesResponse, NewsSource } from '../types';
import { AiBriefBlock } from './AiBriefBlock';
import { Button } from './ui/button';
import { Card, CardContent, CardHeader, CardTitle } from './ui/card';

/**
 * Coverage-gap scout (deterministic triggers, no AI): computed from the loaded
 * window, each suggestion deep-links to the remedy. Points at thin evidence —
 * never fills gaps with generated content.
 */
export function NextSteps({
  data,
  newsSource,
}: {
  data: MovesResponse;
  newsSource: NewsSource;
}) {
  const suggestions: { text: string; to: string; label: string }[] = [];
  const symbol = data.company.symbol;
  const date = data.decisionDate;

  const movesWithoutNews = data.keyMoves.filter(
    (m) => (data.evidenceByDate[m.date]?.news.length ?? 0) === 0,
  );
  if (movesWithoutNews.length > 0 && newsSource !== 'marketaux') {
    suggestions.push({
      text: `${movesWithoutNews.length} key move(s) have no ${newsSource === 'gdelt' ? 'GDELT' : 'Alpha Vantage'} news — a finance-tagged source may cover them.`,
      to: `/moves?symbol=${symbol}&date=${date}&newsSource=marketaux`,
      label: 'Retry with MarketAux',
    });
  }
  const silentSocial = data.keyMoves.filter(
    (m) => (data.evidenceByDate[m.date]?.social.length ?? 0) === 0,
  );
  if (silentSocial.length === data.keyMoves.length && data.keyMoves.length > 0) {
    suggestions.push({
      text: 'No retail discussion surfaced for any move — the community archive is best-effort and throttled.',
      to: `/methodology#limitations`,
      label: 'Read coverage limits',
    });
  }
  suggestions.push({
    text: 'Compare this window against another company on the same cutoff.',
    to: `/compare?symbols=${symbol}&date=${date}&newsSource=${newsSource}`,
    label: 'Open Compare',
  });

  const [brief, setBrief] = useState<ClusterBrief | null>(null);
  const [busy, setBusy] = useState(false);

  if (suggestions.length === 0) return null;

  const explain = () => {
    setBusy(true);
    api
      .copilot('suggest', {
        symbol,
        date,
        newsSource,
        gaps: suggestions.map((s) => `${s.text} [${s.label}]`),
      })
      .then((r) => {
        if (r.brief) setBrief(r.brief);
      })
      .catch(() => undefined)
      .finally(() => setBusy(false));
  };

  return (
    <Card>
      <CardHeader>
        <div className="flex flex-wrap items-center gap-2">
          <CardTitle className="text-base">Suggested next steps</CardTitle>
          <Button size="sm" variant="ghost" disabled={busy} onClick={explain}>
            {busy ? 'Phrasing…' : 'Phrase as brief'}
          </Button>
        </div>
        <p className="text-xs text-fg-dim">
          Computed from this window's coverage — deterministic pointers, not advice.
        </p>
      </CardHeader>
      <CardContent className="space-y-2">
        {brief && <AiBriefBlock brief={brief} context="phrasing of the pointers below" />}
        <ul className="space-y-2 text-sm">
          {suggestions.map((s, i) => (
            <li key={i} className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
              <span className="text-fg-muted">{s.text}</span>
              <Link to={s.to} className="text-xs underline decoration-dotted underline-offset-2 hover:text-fg">
                {s.label}
              </Link>
            </li>
          ))}
        </ul>
      </CardContent>
    </Card>
  );
}
