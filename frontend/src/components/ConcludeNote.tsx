import { useState } from 'react';
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
}: {
  storageKey: string;
  citations: { id: string; label: string }[];
}) {
  const [text, setText] = useState(() => {
    try {
      return localStorage.getItem(storageKey) ?? '';
    } catch {
      return '';
    }
  });
  const [savedAt, setSavedAt] = useState<string | null>(null);

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
        <div className="flex items-center gap-3">
          <Button size="sm" onClick={save}>
            Save note
          </Button>
          {savedAt ? (
            <span className="text-xs text-fg-dim">Saved {savedAt} — this browser only.</span>
          ) : (
            <span className="text-xs text-fg-dim">Unsaved changes.</span>
          )}
        </div>
      </CardContent>
    </Card>
  );
}
