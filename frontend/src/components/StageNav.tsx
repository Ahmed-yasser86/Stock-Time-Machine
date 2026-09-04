import { Link, useLocation, useSearchParams } from 'react-router-dom';

/** Visible journey spine: Setup → Reconstruct → Lens → Compare → Conclude.
 * Every link preserves the full investigation context (symbols, date, source);
 * Conclude scrolls to the note on the Lens page. Rendered on investigation
 * pages only — landing and methodology stay clean entry points. */
export function StageNav() {
  const { pathname } = useLocation();
  const [params] = useSearchParams();
  if (!['/snapshot', '/moves', '/compare'].includes(pathname)) return null;

  const symbol = params.get('symbol')?.trim() ?? '';
  const symbols = params.get('symbols')?.trim() ?? symbol;
  const date = params.get('date')?.trim() ?? '';
  const newsSource = params.get('newsSource')?.trim() ?? 'gdelt';
  if (!date || (!symbol && !symbols)) return null;

  const snap = `/snapshot?symbol=${encodeURIComponent(symbol || symbols.split(',')[0])}&date=${encodeURIComponent(date)}&newsSource=${encodeURIComponent(newsSource)}`;
  const lens = `/moves?symbol=${encodeURIComponent(symbol || symbols.split(',')[0])}&date=${encodeURIComponent(date)}&newsSource=${encodeURIComponent(newsSource)}`;
  const compare = `/compare?symbols=${encodeURIComponent(symbols || symbol)}&date=${encodeURIComponent(date)}&newsSource=${encodeURIComponent(newsSource)}`;

  const stages = [
    { label: 'Setup', to: '/investigate', active: false },
    { label: 'Reconstruct', to: snap, active: pathname === '/snapshot' },
    { label: 'Lens', to: lens, active: pathname === '/moves' },
    { label: 'Compare', to: compare, active: pathname === '/compare' },
    { label: 'Conclude', to: `${lens}#conclude`, active: false },
  ];

  return (
    <nav aria-label="Investigation stages" className="mb-6 flex flex-wrap items-center gap-1 text-sm">
      {stages.map((s, i) => (
        <span key={s.label} className="flex items-center gap-1">
          {i > 0 && (
            <span aria-hidden="true" className="text-fg-dim">
              →
            </span>
          )}
          <Link
            to={s.to}
            aria-current={s.active ? 'step' : undefined}
            className={`rounded-md px-2 py-1 ${
              s.active ? 'bg-accent font-medium text-fg' : 'text-fg-muted hover:text-fg'
            }`}
          >
            {s.label}
          </Link>
        </span>
      ))}
    </nav>
  );
}
