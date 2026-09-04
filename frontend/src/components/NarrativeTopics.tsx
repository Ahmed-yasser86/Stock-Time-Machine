import { fmtDate } from '../lib/format';
import type { NarrativesResponse } from '../types';
import { Alert, AlertDescription, AlertTitle } from './ui/alert';
import { MethodLink } from './MethodLink';
import { Badge } from './ui/badge';
import { Card, CardContent, CardHeader, CardTitle } from './ui/card';
import { Skeleton } from './ui/skeleton';
import { EmptySection, ErrorState } from './StateBlocks';

/**
 * Narrative threads: presentational — the Moves page owns the query so it can
 * drive the staged progress bar. Threads mirror the selected source's entity
 * matching, which can be loose: an off-topic thread means noisy provider
 * tagging, never a claim about the company. Empty cache yields an honest
 * empty state (warmed by snapshot/moves runs).
 */
export function NarrativeTopics({
  query,
}: {
  query: {
    data: NarrativesResponse | undefined;
    isPending: boolean;
    isError: boolean;
    error: unknown;
    refetch: () => void;
  };
}) {
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

  // Plain-object props don't narrow like useQuery's discriminated union;
  // this guard is unreachable (pending/error return above) but keeps TS honest.
  const data = query.data;
  if (!data) return null;

  return (
    <Card>
      <CardHeader>
        <div className="flex flex-wrap items-center gap-2">
          <CardTitle className="text-base">Narrative threads</CardTitle>
          <MethodLink anchor="narrative-topics" />
        </div>
        <p className="text-xs text-fg-dim">
          {data.clusteringMethod === 'gemini-embeddings'
            ? `AI-grouped threads from ${data.articlesConsidered} cached article(s) — embeddings decide membership, shared terms name each thread.`
            : `Keyword-overlap clusters from ${data.articlesConsidered} cached article(s) — top terms
          label each thread, not machine understanding.`}
        </p>
      </CardHeader>
      <CardContent className="space-y-2">
        {data.topics.length === 0 ? (
          <EmptySection
            title="No narrative threads"
            body="No cached news to cluster for this window. Run a snapshot or moves investigation first — coverage warms the cache at zero extra cost."
          />
        ) : (
          <ul className="density-compact space-y-2">
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
                {t.brief && (
                  <div className="mt-2 space-y-1 rounded-md bg-canvas p-2">
                    <p className="flex flex-wrap items-center gap-2 text-xs">
                      <Badge variant="outline">AI brief · {t.brief.model}</Badge>
                      <span className="text-fg-dim">generated, non-deterministic — verify against the articles</span>
                    </p>
                    <p className="text-sm">{t.brief.summary}</p>
                    {t.brief.keyPoints.length > 0 && (
                      <ul className="list-disc space-y-0.5 pl-5 text-sm">
                        {t.brief.keyPoints.map((k, j) => (
                          <li key={j}>{k}</li>
                        ))}
                      </ul>
                    )}
                  </div>
                )}
              </li>
            ))}
          </ul>
        )}
        {data.topics.length > 0 && (
          <Alert>
            <AlertTitle className="text-xs">Reading guide</AlertTitle>
            <AlertDescription className="text-xs">
              Threads group what the selected source returned for this company — its entity
              matching can be loose, so an off-topic thread reflects noisy provider tagging,
              not a claim about the company. Threads show what was being written about
              together — not what matters most, and never why prices moved.
            </AlertDescription>
          </Alert>
        )}
      </CardContent>
    </Card>
  );
}
