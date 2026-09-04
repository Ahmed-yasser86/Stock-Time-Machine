import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { X } from 'lucide-react';
import { api } from '../lib/api';
import { MAX_COMPARE_PICKS, pickColor } from '../lib/palette';
import { Badge } from './ui/badge';
import { Input } from './ui/input';

/**
 * Multi-company picker: combobox search (reuses company-search) + removable
 * chips, capped at MAX_COMPARE_PICKS. Emits uppercase symbols; dumb about
 * dates — the page owns the decision date.
 */
export function SymbolPicker({
  picks,
  onChange,
}: {
  picks: string[];
  onChange: (next: string[]) => void;
}) {
  const [query, setQuery] = useState('');
  const search = useQuery({
    queryKey: ['company-search', query.trim()],
    queryFn: () => api.companySearch(query.trim()),
    enabled: query.trim().length >= 1,
    staleTime: 5 * 60_000,
  });
  const results = (search.data ?? []).filter((c) => !picks.includes(c.symbol.toUpperCase()));

  const add = (symbol: string) => {
    const s = symbol.toUpperCase();
    if (!picks.includes(s) && picks.length < MAX_COMPARE_PICKS) {
      onChange([...picks, s]);
    }
    setQuery('');
  };

  return (
    <div className="space-y-2">
      <div className="flex flex-wrap gap-2" aria-label="Selected companies">
        {picks.map((s, i) => (
          <Badge
            key={s}
            variant="secondary"
            className="gap-1 font-mono"
            style={{ borderLeft: `4px solid ${pickColor(i)}` }}
          >
            {s}
            <button
              type="button"
              onClick={() => onChange(picks.filter((p) => p !== s))}
              aria-label={`Remove ${s}`}
              className="ml-1 rounded p-0.5 hover:bg-accent"
            >
              <X className="size-3" aria-hidden="true" />
            </button>
          </Badge>
        ))}
        {picks.length === 0 && <span className="text-sm text-fg-dim">No companies selected yet.</span>}
      </div>
      {picks.length < MAX_COMPARE_PICKS ? (
        <div>
          <Input
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Add a company — type to search…"
            aria-label="Add a company by name or symbol"
          />
          {query.trim() !== '' && (
            <ul className="mt-1 max-h-44 space-y-1 overflow-y-auto rounded-lg border border-border bg-surface p-1">
              {(results ?? []).slice(0, 8).map((c) => (
                <li key={c.symbol}>
                  <button
                    type="button"
                    onClick={() => add(c.symbol)}
                    className="flex w-full items-baseline gap-2 rounded px-2 py-1 text-left text-sm hover:bg-accent"
                  >
                    <span className="font-mono font-medium">{c.symbol}</span>
                    <span className="truncate text-fg-muted">{c.name}</span>
                  </button>
                </li>
              ))}
              {results.length === 0 && !search.isPending && (
                <li className="px-2 py-1 text-sm text-fg-dim">No company found.</li>
              )}
            </ul>
          )}
        </div>
      ) : (
        <p className="text-xs text-fg-dim">
          {MAX_COMPARE_PICKS} companies max — each pick multiplies provider cost and load time.
        </p>
      )}
    </div>
  );
}
