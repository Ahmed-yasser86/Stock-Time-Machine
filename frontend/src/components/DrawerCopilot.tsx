import { useState } from 'react';
import { api } from '../lib/api';
import type { ClusterBrief, NewsSource } from '../types';
import { AiBriefBlock } from './AiBriefBlock';
import { Button } from './ui/button';

/**
 * Drawer evidence copilot: explicit buttons over THIS move's evidence only.
 * Never auto-run; failures render as honest unavailability, never errors.
 */
export function DrawerCopilot({
  symbol,
  moveDate,
  newsSource,
  filingCount,
  newsIds,
}: {
  symbol: string;
  moveDate: string;
  newsSource: NewsSource;
  filingCount: number;
  newsIds: string[];
}) {
  const [brief, setBrief] = useState<ClusterBrief | null>(null);
  const [kind, setKind] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);

  const run = async (action: 'filings-summary' | 'contrast' | 'explain-uncertainty') => {
    setBusy(true);
    setFailed(false);
    setBrief(null);
    try {
      const res = await api.copilot(action, {
        symbol,
        date: moveDate,
        newsSource,
        ids: action === 'contrast' ? newsIds.slice(0, 5) : undefined,
      });
      if (res.brief) {
        setBrief(res.brief);
        setKind(
          action === 'contrast'
            ? 'article contrast'
            : action === 'explain-uncertainty'
              ? 'uncertainty reading for this window'
              : 'filing summary',
        );
      } else {
        setFailed(true);
      }
    } catch {
      setFailed(true);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="space-y-2">
      <div className="flex flex-wrap gap-2">
        {filingCount > 0 && (
          <Button size="sm" variant="outline" disabled={busy} onClick={() => run('filings-summary')}>
            Summarize filings
          </Button>
        )}
        {newsIds.length >= 2 && (
          <Button size="sm" variant="outline" disabled={busy} onClick={() => run('contrast')}>
            Contrast articles
          </Button>
        )}
        <Button size="sm" variant="outline" disabled={busy} onClick={() => run('explain-uncertainty')}>
          Explain window uncertainty
        </Button>
      </div>
      {busy && (
        <p className="text-xs text-fg-dim" aria-busy="true">
          Grounding in this move's evidence and briefing…
        </p>
      )}
      {failed && !busy && (
        <p className="text-xs text-fg-dim">
          No copilot output — the evidence above stands on its own.
        </p>
      )}
      {brief && kind && <AiBriefBlock brief={brief} context={kind} />}
    </div>
  );
}
