import { useEffect, useId, useRef, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { Building2, CalendarDays, Newspaper, ShieldCheck, X } from 'lucide-react';
import { api, apiErrorMessage } from '../lib/api';
import { fmtDate, todayLocal } from '../lib/format';
import { newsSourceLabel, type Company, type NewsSource } from '../types';
import { getRecentInvestigations } from '../lib/recentInvestigations';
import { Button, buttonVariants } from '../components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../components/ui/card';
import { Input } from '../components/ui/input';
import { Label } from '../components/ui/label';
import { Badge } from '../components/ui/badge';
import { Separator } from '../components/ui/separator';
import { Alert, AlertDescription, AlertTitle } from '../components/ui/alert';

function useDebounced(value: string, ms: number): string {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const t = setTimeout(() => setDebounced(value), ms);
    return () => clearTimeout(t);
  }, [value, ms]);
  return debounced;
}

const QUICK: { symbol: string; name: string; date: string }[] = [
  { symbol: 'TSLA', name: 'Tesla, Inc.', date: '2020-01-15' },
  { symbol: 'AAPL', name: 'Apple Inc.', date: '2019-01-03' },
  { symbol: 'MSFT', name: 'Microsoft Corporation', date: '2020-03-16' },
];

export default function Investigate() {
  const navigate = useNavigate();
  const [query, setQuery] = useState('');
  const [selected, setSelected] = useState<Company | null>(null);
  const [date, setDate] = useState('');
  const [newsSource, setNewsSource] = useState<NewsSource>('gdelt');
  const [open, setOpen] = useState(false);
  const [activeIndex, setActiveIndex] = useState(-1);
  const listId = useId();
  const boxRef = useRef<HTMLDivElement>(null);

  const debounced = useDebounced(query.trim(), 300);
  const search = useQuery({
    queryKey: ['company-search', debounced],
    queryFn: () => api.companySearch(debounced),
    enabled: debounced.length >= 1 && !selected,
    placeholderData: keepPreviousData,
    staleTime: 5 * 60_000,
  });
  const results = search.data ?? [];
  // Clicking the empty input shows choosable suggestions (the known quick
  // investigations); typing switches to live search results. One visible list
  // drives display, keyboard, and selection together.
  const suggestions: Company[] = QUICK.map((q) => ({
    symbol: q.symbol, name: q.name, cik: '', exchange: '', sector: '', industry: '',
  }));
  const visible = debounced === '' ? suggestions : results;

  useEffect(() => {
    const onDown = (e: MouseEvent) => {
      if (boxRef.current && !boxRef.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', onDown);
    return () => document.removeEventListener('mousedown', onDown);
  }, []);

  const today = todayLocal();
  const dateError =
    !date || !selected
      ? null
      : date >= today
        ? 'Please select a date in the past.'
        : null;

  const canStart = selected !== null && date !== '' && !dateError;

  function choose(c: Company) {
    setSelected(c);
    setQuery('');
    setOpen(false);
  }

  function start() {
    if (!canStart || !selected) return;
    navigate(
      `/snapshot?symbol=${encodeURIComponent(selected.symbol)}&date=${encodeURIComponent(date)}&newsSource=${newsSource}`,
    );
  }

  return (
    <div className="mx-auto max-w-3xl space-y-6">
      <div className="space-y-2">
        <h1 className="font-display text-3xl font-semibold tracking-tight">Start an investigation</h1>
        <p className="text-sm text-fg-muted">
          Pick a company and a moment in its history. The engine reconstructs only what was
          publicly knowable on or before that date.{' '}
          <Link
            to="/snapshot?symbol=TSLA&date=2020-01-15&guided=1"
            className="underline decoration-dotted underline-offset-2 hover:text-fg"
          >
            First time? Take the guided tour.
          </Link>
        </p>
      </div>

      {/* Step 1 — company */}
      {/* overflow-visible: the card's default overflow-hidden would clip the
          absolutely-positioned suggestion dropdown (options were in the DOM
          but invisible to users). Scoped to this card only. */}
      <Card className="overflow-visible">
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <Building2 className="size-4 text-primary" aria-hidden="true" /> 1. Find the company
          </CardTitle>
        </CardHeader>
        <CardContent className="space-y-3">
          {selected ? (
            <div className="flex flex-wrap items-center gap-2">
              <Badge variant="secondary" className="font-mono text-sm">
                {selected.symbol} · {selected.name}
              </Badge>
              {selected.exchange && <span className="text-xs text-fg-dim">{selected.exchange}</span>}
              <Button variant="ghost" size="sm" onClick={() => setSelected(null)}>
                <X aria-hidden="true" /> Clear
              </Button>
            </div>
          ) : (
            <div ref={boxRef} className="relative">
              <Label htmlFor="company-search">Company name or ticker</Label>
              <Input
                id="company-search"
                role="combobox"
                aria-expanded={open}
                aria-controls={listId}
                aria-activedescendant={activeIndex >= 0 ? `${listId}-${activeIndex}` : undefined}
                autoComplete="off"
                placeholder="Tesla or TSLA…"
                value={query}
                onChange={(e) => {
                  setQuery(e.target.value);
                  setActiveIndex(-1);
                  setOpen(true);
                }}
                onFocus={() => {
                  if (!selected) {
                    setActiveIndex(-1);
                    setOpen(true);
                  }
                }}
                onKeyDown={(e) => {
                  if (e.key === 'ArrowDown' && visible.length > 0) {
                    e.preventDefault();
                    setActiveIndex((i) => (i + 1) % visible.length);
                  } else if (e.key === 'ArrowUp' && visible.length > 0) {
                    e.preventDefault();
                    setActiveIndex((i) => (i - 1 + visible.length) % visible.length);
                  } else if (e.key === 'Enter' && activeIndex >= 0 && visible[activeIndex]) {
                    e.preventDefault();
                    choose(visible[activeIndex]);
                  } else if (e.key === 'Escape') {
                    setOpen(false);
                  }
                }}
              />
              {open && (
                <div
                  id={listId}
                  role="listbox"
                  aria-label={debounced === '' ? 'Suggested companies' : 'Matching companies'}
                  className="absolute z-10 mt-1 max-h-64 w-full overflow-auto rounded-lg border border-border bg-popover shadow-lg"
                >
                  {debounced === '' && (
                    <p className="px-3 pt-2 text-xs text-fg-dim">
                      Popular starting points — or type to search all companies.
                    </p>
                  )}
                  {debounced !== '' && search.isPending && <p className="px-3 py-2 text-sm text-fg-muted">Searching…</p>}
                  {debounced !== '' && search.isError && (
                    <p className="px-3 py-2 text-sm text-destructive">
                      {apiErrorMessage(search.error, 'Company search failed.')}
                    </p>
                  )}
                  {debounced !== '' && search.isSuccess && visible.length === 0 && (
                    <p className="px-3 py-2 text-sm text-fg-muted">
                      No company found matching &lsquo;{debounced}&rsquo;. Try a different name or ticker symbol.
                    </p>
                  )}
                  {visible.map((c, i) => (
                    <button
                      key={c.symbol}
                      id={`${listId}-${i}`}
                      role="option"
                      aria-selected={i === activeIndex}
                      type="button"
                      className={`flex w-full flex-col px-3 py-2 text-left text-sm hover:bg-accent ${i === activeIndex ? 'bg-accent' : ''}`}
                      onClick={() => choose(c)}
                      onMouseEnter={() => setActiveIndex(i)}
                    >
                      <span className="font-medium">
                        {c.name} <span className="font-mono text-fg-muted">{c.symbol}</span>
                      </span>
                      {c.exchange && <span className="text-xs text-fg-dim">{c.exchange}</span>}
                    </button>
                  ))}
                </div>
              )}
            </div>
          )}
        </CardContent>
      </Card>

      {/* Step 2 — date + news source */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <CalendarDays className="size-4 text-primary" aria-hidden="true" /> 2. Choose the moment and the evidence
          </CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="inv-date">Historical date</Label>
            <Input
              id="inv-date"
              type="date"
              value={date}
              max={today}
              onChange={(e) => setDate(e.target.value)}
              aria-invalid={dateError ? true : undefined}
              aria-describedby={dateError ? 'inv-date-error' : undefined}
            />
            {dateError && (
              <p id="inv-date-error" role="alert" className="text-sm text-destructive">
                {dateError}
              </p>
            )}
            <p className="text-xs text-fg-dim">
              Future dates and today are not selectable. If markets were closed on your date,
              the snapshot shows the previous trading day and says so.
            </p>
          </div>

          <Separator />

          <fieldset>
            <legend className="flex items-center gap-2 text-sm font-medium">
              <Newspaper className="size-4 text-primary" aria-hidden="true" /> News source
            </legend>
            <p className="mt-1 text-xs text-fg-dim">
              Your choice is explicit and respected: results come from one source only, with
              source attribution on every item. Sources are never mixed or substituted.
            </p>
            <div className="mt-3 grid gap-2 sm:grid-cols-3" role="radiogroup" aria-label="News source">
              {(['gdelt', 'alphavantage', 'marketaux'] as NewsSource[]).map((s) => (
                <button
                  key={s}
                  type="button"
                  role="radio"
                  aria-checked={newsSource === s}
                  onClick={() => setNewsSource(s)}
                  className={`rounded-lg border p-3 text-left text-sm transition-colors ${
                    newsSource === s
                      ? 'border-ring bg-accent'
                      : 'border-border hover:border-fg-dim'
                  }`}
                >
                  <span className="font-medium">{newsSourceLabel(s)}</span>
                  <span className="mt-1 block text-xs text-fg-muted">
                    {s === 'gdelt'
                      ? 'World news archive. Broad coverage, best-effort completeness.'
                      : s === 'alphavantage'
                        ? 'Market-aware news feed. Finance-focused, best-effort completeness.'
                        : 'Entity-tagged finance news with sentiment. Recent years only.'}
                  </span>
                </button>
              ))}
            </div>
          </fieldset>
        </CardContent>
      </Card>

      {/* Step 3 — confirm */}
      {selected && date && !dateError && (
        <Alert>
          <ShieldCheck />
          <AlertTitle>Confirm your investigation</AlertTitle>
          <AlertDescription className="space-y-3">
            <p>
              You are investigating <strong>{selected.name} ({selected.symbol})</strong> as of{' '}
              <strong>{fmtDate(date)}</strong>. You will only see information that was publicly
              available on or before that date — cutoff {fmtDate(date)}, 23:59:59 US/Eastern.
              News comes from {newsSourceLabel(newsSource)}.
            </p>
            <Button onClick={start}>Start investigation</Button>
          </AlertDescription>
        </Alert>
      )}

      {/* Quick investigations */}
      <Card>
        <CardHeader>
          <CardTitle className="text-base">Or reopen a known moment</CardTitle>
          <CardDescription>Real companies, real dates — full reconstructions.</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-wrap gap-2">
          {QUICK.map((q) => (
            <Link
              key={q.symbol + q.date}
              to={`/snapshot?symbol=${q.symbol}&date=${q.date}&newsSource=gdelt`}
              className={buttonVariants({ variant: 'outline', size: 'sm' })}
            >
              {q.symbol} · {fmtDate(q.date)}
            </Link>
          ))}
        </CardContent>
      </Card>

      {/* Recent investigations (session memory, local only) */}
      {getRecentInvestigations().length > 0 && (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Pick up where you left off</CardTitle>
            <CardDescription>This session only — stored in your browser, never sent anywhere.</CardDescription>
          </CardHeader>
          <CardContent className="flex flex-wrap gap-2">
            {getRecentInvestigations().map((r) => (
              <Link
                key={`${r.symbol}|${r.date}|${r.newsSource}`}
                to={`/snapshot?symbol=${r.symbol}&date=${r.date}&newsSource=${r.newsSource}`}
                className={buttonVariants({ variant: 'outline', size: 'sm' })}
              >
                {r.symbol} · {fmtDate(r.date)} · {newsSourceLabel(r.newsSource)}
              </Link>
            ))}
          </CardContent>
        </Card>
      )}
    </div>
  );
}
