import { useState } from 'react';
import { Link } from 'react-router-dom';
import { ExternalLink } from 'lucide-react';
import { fmtDate } from '../lib/format';
import { whyNoNews } from '../lib/whyEmpty';
import { NEWS_COVERAGE_DISCLAIMER, newsSourceLabel, type Disclosure, type Filing, type NewsItem, type NewsSource } from '../types';
import { Alert, AlertDescription } from './ui/alert';
import { Badge } from './ui/badge';
import { buttonVariants } from './ui/button';
import { EmptySection } from './StateBlocks';

type Filter = 'all' | 'filings' | 'disclosures' | 'news';

function ProvenanceChip({ source, date, url }: { source: string; date: string; url?: string }) {
  return (
    <p className="mt-1 text-xs text-fg-muted">
      {source} · {fmtDate(date)}
      {url && (
        <>
          {' · '}
          <a
            href={url}
            target="_blank"
            rel="noreferrer"
            className="inline-flex items-center gap-1 text-primary underline-offset-4 hover:underline"
          >
            SEC.gov <ExternalLink className="size-3" aria-hidden="true" />
          </a>
        </>
      )}
    </p>
  );
}

interface StreamItem {
  key: string;
  kind: Filter;
  date: string;
  node: React.ReactNode;
}

/**
 * Evidence stream: all historical evidence in one time-ordered flow with
 * provenance on every item, replacing the tabbed view. Filter chips narrow
 * by category; counts stay visible so absence reads as fact, not bug.
 */
export function EvidenceStream({
  filings,
  disclosures,
  news,
  newsSource,
  asOfDate,
}: {
  filings: Filing[];
  disclosures: Disclosure[];
  news: NewsItem[];
  newsSource: NewsSource;
  asOfDate: string;
}) {
  const [filter, setFilter] = useState<Filter>('all');

  const items: StreamItem[] = [
    ...filings.map((f) => ({
      key: f.accessionNumber,
      kind: 'filings' as const,
      date: f.filedAt,
      node: (
        <div>
          <p className="flex flex-wrap items-center gap-2">
            <Badge variant="secondary" className="font-mono">{f.formType}</Badge>
            <span className="font-medium">Regulatory filing</span>
          </p>
          {f.summary && <p className="mt-1 text-fg-muted">{f.summary}</p>}
          <ProvenanceChip source="SEC EDGAR" date={f.filedAt} url={f.url} />
        </div>
      ),
    })),
    ...disclosures.map((d) => ({
      key: d.accessionNumber,
      kind: 'disclosures' as const,
      date: d.filedAt,
      node: (
        <div>
          <p className="flex flex-wrap items-center gap-2">
            <Badge variant="secondary" className="font-mono">{d.formType}</Badge>
            <span className="font-medium">{d.title}</span>
          </p>
          <p className="mt-1 text-xs text-fg-dim">Material-event disclosure — regulatory evidence, not a news article.</p>
          <ProvenanceChip source="SEC EDGAR" date={d.filedAt} url={d.url} />
        </div>
      ),
    })),
    ...news.map((n, i) => ({
      key: `${n.url}-${i}`,
      kind: 'news' as const,
      date: n.publishedAt,
      node: (
        <div>
          <p>
            <a href={n.url} target="_blank" rel="noreferrer" className="font-medium underline-offset-4 hover:underline">
              {n.title}
            </a>
          </p>
          <ProvenanceChip source={n.source} date={n.publishedAt} />
        </div>
      ),
    })),
  ]
    .filter((item) => filter === 'all' || item.kind === filter)
    .sort((a, b) => (a.date < b.date ? 1 : -1));

  const counts: Record<Filter, number> = {
    all: filings.length + disclosures.length + news.length,
    filings: filings.length,
    disclosures: disclosures.length,
    news: news.length,
  };
  const labels: Record<Filter, string> = {
    all: 'All evidence',
    filings: 'Regulatory filings',
    disclosures: 'Corporate disclosures',
    news: `Historical news · ${newsSourceLabel(newsSource)}`,
  };

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap gap-2" role="group" aria-label="Filter evidence by category">
        {(Object.keys(counts) as Filter[]).map((f) => (
          <button
            key={f}
            type="button"
            onClick={() => setFilter(f)}
            aria-pressed={filter === f}
            className={`rounded-full border px-3 py-1 text-sm transition-colors ${
              filter === f
                ? 'border-primary bg-primary text-primary-foreground'
                : 'border-border bg-surface text-fg-muted hover:border-primary hover:text-fg'
            }`}
          >
            {labels[f]} ({counts[f]})
          </button>
        ))}
      </div>

      {(filter === 'all' || filter === 'news') && (
        <Alert>
          <AlertDescription>{NEWS_COVERAGE_DISCLAIMER}</AlertDescription>
        </Alert>
      )}

      {items.length === 0 ? (
        <EmptySection
          title={filter === 'news' ? 'No historical news found' : 'Nothing in this category'}
          body={
            filter === 'news'
              ? whyNoNews(newsSource, fmtDate(asOfDate))
              : 'No evidence of this kind was available before the selected date.'
          }
          action={
            filter === 'news' ? (
              <Link to="/methodology" className={buttonVariants({ variant: 'outline', size: 'sm' })}>
                Read about coverage limits
              </Link>
            ) : undefined
          }
        />
      ) : (
        <ul className="density-compact evidence-rail space-y-2">
          {items.map((item) => (
            <li key={item.key} className="rounded-lg border border-border bg-surface p-3 text-sm">
              {item.node}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
