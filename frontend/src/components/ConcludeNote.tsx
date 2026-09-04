import { useState } from 'react';
import { api } from '../lib/api';
import type { NewsSource, NoteIssue } from '../types';
import { Button } from './ui/button';
import { Card, CardContent, CardHeader, CardTitle } from './ui/card';

/**
 * Conclude stage (Plan 2): the product never concludes — the user does, with
 * citations. A private note (localStorage, per investigation key) with
 * one-click citation chips ([move DATE], [thread terms]) pointing back at the
 * evidence above. Nothing leaves the browser; print stylesheet covers export.
 */
export function ConcludeNote({
  storageKey,
  citations,
  symbol,
  date,
  newsSource,
}: {
  storageKey: string;
  citations: { id: string; label: string }[];
  symbol: string;
  date: string;
  newsSource: NewsSource;
}) {
  const [text, setText] = useState(() => {
    try {
      return localStorage.getItem(storageKey) ?? '';
    } catch {
      return '';
    }
  });
  const [savedAt, setSavedAt] = useState<string | null>(null);
  const [issues, setIssues] = useState<NoteIssue[] | null>(null);
  const [checking, setChecking] = useState(false);
  const [checkFailed, setCheckFailed] = useState(false);

  const check = () => {
    setChecking(true);
    setCheckFailed(false);
    setIssues(null);
    api
      .reviewNote({ symbol, date, newsSource, note: text })
      .then((r) => setIssues(r.issues))
      .catch(() => setCheckFailed(true))
      .finally(() => setChecking(false));
  };

  const save = () => {
    try {
      localStorage.setItem(storageKey, text);
      setSavedAt(new Date().toLocaleTimeString());
    } catch {
      setSavedAt(null);
    }
  };

  const insert = (id: string) => {
    setText((t) => (t.endsWith(' ') || t === '' ? t : t + ' ') + `[${id}] `);
    setSavedAt(null);
  };

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Conclude — your note, your citations</CardTitle>
        <p className="text-xs text-fg-dim">
          The product presents evidence; conclusions are yours. Cite moves and threads with the
          chips — private to this browser, printable via your browser's print.
        </p>
      </CardHeader>
      <CardContent className="space-y-3">
        {citations.length > 0 && (
          <div className="flex flex-wrap gap-1.5" aria-label="Insert citation">
            {citations.map((c) => (
              <button
                key={c.id}
                type="button"
                onClick={() => insert(c.id)}
                title={c.label}
                className="rounded border border-border px-2 py-0.5 font-mono text-xs text-fg-muted hover:border-fg-dim hover:text-fg"
              >
                [{c.id.length > 28 ? c.id.slice(0, 28) + '…' : c.id}]
              </button>
            ))}
          </div>
        )}
        <textarea
          value={text}
          onChange={(e) => {
            setText(e.target.value);
            setSavedAt(null);
          }}
          rows={5}
          aria-label="Your conclusion note"
          placeholder="e.g. Move 2026-06-26 [move 2026-06-26] coincided with the heaviest news layer [thread data · center · microsoft]…"
          className="w-full rounded-lg border border-border bg-surface p-3 text-sm"
        />
        <div className="flex flex-wrap items-center gap-3">
          <Button size="sm" onClick={save}>
            Save note
          </Button>
          <Button size="sm" variant="outline" onClick={check} disabled={checking || text.trim() === ''}>
            {checking ? 'Checking…' : 'Check my citations'}
          </Button>
          {savedAt ? (
            <span className="text-xs text-fg-dim">Saved {savedAt} — this browser only.</span>
          ) : (
            <span className="text-xs text-fg-dim">Unsaved changes.</span>
          )}
        </div>
        {checkFailed && !checking && (
          <p className="text-xs text-fg-dim">
            Citation check unavailable — re-read your chips against the evidence above.
          </p>
        )}
        {issues && (
          <div className="space-y-1 rounded-md border border-border p-2" aria-label="Citation check results">
            <p className="text-xs text-fg-dim">
              Reviewer report — checks your claims against the evidence ledger. It reviews; it never rewrites.
            </p>
            {issues.length === 0 ? (
              <p className="text-sm">No cited claims found to check. Add citations with the chips above.</p>
            ) : (
              <ul className="space-y-1 text-sm">
                {issues.map((iss, i) => (
                  <li key={i} className="flex flex-wrap gap-x-2">
                    <span className="font-mono">[{iss.ref}]</span>
                    <strong
                      className={
                        iss.verdict === 'supported'
                          ? 'text-gain'
                          : iss.verdict === 'unsupported'
                            ? 'text-loss'
                            : 'text-fg-muted'
                      }
                    >
                      {iss.verdict}
                    </strong>
                    <span className="text-fg-muted">{iss.detail}</span>
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
