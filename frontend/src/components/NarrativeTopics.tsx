import { useQuery } from '@tanstack/react-query';
import { api } from '../lib/api';
import { fmtDate } from '../lib/format';
import type { NewsSource } from '../types';
import { Alert, AlertDescription, AlertTitle } from './ui/alert';
import { Badge } from './ui/badge';
import { Card, CardContent, CardHeader, CardTitle } from './ui/card';
import { Skeleton } from './ui/skeleton';
import { EmptySection, ErrorState } from './StateBlocks';

/**
 * Narrative threads: keyword-overlap clusters (TF-IDF + cosine) over cached
 * news text. Labels are top terms, not semantic understanding — the UI says so.
 * Empty cache yields an honest empty state (warmed by snapshot/moves runs).
 */
export function NarrativeTopics({
  symbol,
  date,
  newsSource,
}: {
  symbol: string;
  date: string;
  newsSource: NewsSource;
}) {
  const query = useQuery({
    queryKey: ['narratives', symbol.toUpperCase(), date, newsSource],
    queryFn: () => api.narratives(symbol, date, newsSource),
    staleTime: 5 * 60_000,
  });

  if (query.isPending) {
    return (
      <Card aria-busy="true" aria-label="Loading narrative topics">
        <CardContent className="space-y-2 pt-6">
          <Skeleton className="h-5 w-1/3" />
          <Skeleton className="h-16 w-full" />
        </CardContent>
      </Card>
    );
  }

  if (query.isError) {
    return (
      <ErrorState
        error={query.error}
        fallback="Narrative topics could not be loaded."
        onRetry={() => query.refetch()}
      />
    );
  }

  const data = query.data;

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Narrative threads</CardTitle>
        <p className="text-xs text-fg-dim">
          Keyword-overlap clusters from {data.articlesConsidered} cached article(s) — top terms
          label each thread, not machine understanding.
        </p>
      </CardHeader>
      <CardContent className="space-y-2">
        {data.topics.length === 0 ? (
          <EmptySection
            title="No narrative threads"
            body="No cached news to cluster for this window. Run a snapshot or moves investigation first — coverage warms the cache at zero extra cost."
          />
        ) : (
          <ul className="space-y-2">
            {data.topics.map((t, i) => (
              <li key={t.labelTerms.join('|') + i} className="rounded-lg border border-border p-3 text-sm">
                <p className="flex flex-wrap items-center gap-2">
                  <Badge variant="secondary">Thread {i + 1}</Badge>
                  <span className="font-mono">{t.labelTerms.join(' · ')}</span>
                  <span className="text-xs text-fg-dim">
                    {t.articleIds.length} article(s)
                    {t.spanStart && t.spanEnd ? ` · ${fmtDate(t.spanStart)} → ${fmtDate(t.spanEnd)}` : ''}
                  </span>
                </p>
                <p className="mt-1 text-fg-muted">e.g. {t.representativeTitle}</p>
              </li>
            ))}
          </ul>
        )}
        {data.topics.length > 0 && (
          <Alert>
            <AlertTitle className="text-xs">Reading guide</AlertTitle>
            <AlertDescription className="text-xs">
              Threads group articles sharing distinctive words. They show what was being written
              about together — not what matters most, and never why prices moved.
            </AlertDescription>
          </Alert>
        )}
      </CardContent>
    </Card>
  );
}
