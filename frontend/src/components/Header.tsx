import { useState } from 'react';
import { Link, NavLink, useLocation, useSearchParams } from 'react-router-dom';
import { History, Menu, X } from 'lucide-react';
import { Button } from './ui/button';
import { Badge } from './ui/badge';
import { Separator } from './ui/separator';
import { fmtDate } from '../lib/format';
import { newsSourceLabel, type NewsSource } from '../types';

function ActiveInvestigation() {
  const { pathname } = useLocation();
  const [params] = useSearchParams();
  if (pathname !== '/snapshot') return null;
  const symbol = params.get('symbol');
  const date = params.get('date');
  if (!symbol || !date) return null;
  const newsSource = (params.get('newsSource') as NewsSource | null) ?? 'gdelt';
  return (
    <div className="flex min-w-0 items-center gap-2" aria-label="Active investigation">
      <Separator orientation="vertical" className="hidden h-6 sm:block" />
      <Badge variant="secondary" className="max-w-full truncate font-mono">
        {symbol.toUpperCase()} · {fmtDate(date)}
      </Badge>
      <Badge variant="outline" className="hidden shrink-0 md:inline-flex">
        {newsSourceLabel(newsSource)}
      </Badge>
    </div>
  );
}

export function Header() {
  const [open, setOpen] = useState(false);
  const linkCls = ({ isActive }: { isActive: boolean }) =>
    `rounded-md px-3 py-2 text-sm font-medium transition-colors ${
      isActive ? 'bg-accent text-fg' : 'text-fg-muted hover:bg-accent hover:text-fg'
    }`;

  return (
    <header className="sticky top-0 z-40 border-b border-border bg-bg/95 backdrop-blur">
      <div className="mx-auto flex h-16 max-w-6xl items-center gap-3 px-4">
        <Link to="/" className="flex shrink-0 items-center gap-2" aria-label="Stock Time Machine home">
          <span className="flex size-8 items-center justify-center rounded-lg bg-primary">
            <History className="size-5 text-primary-foreground" aria-hidden="true" />
          </span>
          <span className="hidden text-sm font-semibold tracking-tight min-[400px]:block">
            Stock Time Machine
          </span>
        </Link>

        <ActiveInvestigation />

        <nav className="ml-auto hidden items-center gap-1 md:flex" aria-label="Primary">
          <NavLink to="/investigate" className={linkCls}>
            New investigation
          </NavLink>
          <NavLink to="/methodology" className={linkCls}>
            Methodology
          </NavLink>
        </nav>

        <Button
          variant="ghost"
          size="icon"
          className="ml-auto md:hidden"
          onClick={() => setOpen((v) => !v)}
          aria-expanded={open}
          aria-label={open ? 'Close navigation' : 'Open navigation'}
        >
          {open ? <X /> : <Menu />}
        </Button>
      </div>

      {open && (
        <nav className="border-t border-border px-4 py-2 md:hidden" aria-label="Mobile">
          <NavLink to="/investigate" className={linkCls} onClick={() => setOpen(false)}>
            <span className="block">New investigation</span>
          </NavLink>
          <NavLink to="/methodology" className={linkCls} onClick={() => setOpen(false)}>
            <span className="block">Methodology</span>
          </NavLink>
        </nav>
      )}
    </header>
  );
}
