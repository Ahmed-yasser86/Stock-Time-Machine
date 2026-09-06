import { useEffect, useState } from 'react';
import { api } from '../lib/api';
import { fmtDate } from '../lib/format';
import { whyNoThreads } from '../lib/whyEmpty';
import type { ClusterBrief, NarrativesResponse, NewsCandidate, NewsSource } from '../types';
import { AiBriefBlock } from './AiBriefBlock';
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
const NON_ASCII = /[^\x00-\x7F]/;

/** English gist for non-English threads: explicit opt-in, same AI contract. */
function ThreadGist({
  symbol,
  date,
  newsSource,
  articleIds,
}: {
  symbol: string;
  date: string;
  newsSource: NewsSource;
  articleIds: string[];
}) {
  const [brief, setBrief] = useState<ClusterBrief | null>(null);
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);

  if (brief) return <AiBriefBlock brief={brief} context="English gist of non-English coverage" />;

  return (
    <div className="mt-2">
      <button
        type="button"
        disabled={busy}
        onClick={() => {
          setBusy(true);
          setFailed(false);
          api
            .copilot('gist', { symbol, date, newsSource, ids: articleIds.slice(0, 5) })
            .then((r) => {
              if (r.brief) setBrief(r.brief);
              else setFailed(true);
            })
            .catch(() => setFailed(true))
            .finally(() => setBusy(false));
        }}
        className="text-xs underline decoration-dotted underline-offset-2 hover:text-fg disabled:opacity-50"
      >
        {busy ? 'Rendering gist…' : 'English gist'}
      </button>
      {failed && !busy && (
        <p className="mt-1 text-xs text-fg-dim">No gist available — the original stands on its own.</p>
      )}
    </div>
  );
}

/**
 * Borderline calls: the relevance gate refused to decide these on its own,
 * so they wait here instead of silently entering — or vanishing from —
 * threads and evidence. Approving admits an article to the normal pipeline;
 * rejecting marks it irrelevant. Either way the verdict carries your name.
 */
function CandidateReview({
  symbol,
  date,
  newsSource,
  onDecided,
}: {
  symbol: string;
  date: string;
  newsSource: NewsSource;
  onDecided: () => void;
}) {
  const [items, setItems] = useState<NewsCandidate[] | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  useEffect(() => {
    let live = true;
    setItems(null);
    api
      .candidates(symbol, date, newsSource)
      .then((r) => {
        if (live) setItems(r.items);
      })
      .catch(() => {
        if (live) setItems([]);
      });
    return () => {
      live = false;
    };
  }, [symbol, date, newsSource]);

  if (items === null || items.length === 0) return null;

  const decide = (id: string, kind: 'approve' | 'reject') => {
    setBusy(id + kind);
    (kind === 'approve' ? api.approveCandidate(id, symbol) : api.rejectCandidate(id, symbol))
      .then(() => {
        setItems((prev) => (prev ?? []).filter((c) => c.article.id !== id));
        onDecided();
      })
      .finally(() => setBusy(null));
  };

  return (
    <div className="rounded-lg border border-dashed border-border p-3">
      <p className="text-sm font-medium">Borderline calls — {items.length} awaiting your review</p>
      <p className="mt-0.5 text-xs text-fg-dim">
        The gate could not call these confidently. Nothing here entered threads or evidence.
      </p>
      <ul className="mt-2 space-y-2">
        {items.map((c) => (
          <li key={c.article.id} className="rounded-md border border-border p-2 text-sm">
            <p className="font-medium">{c.article.title}</p>
            <p className="mt-0.5 text-xs text-fg-dim">
              {c.category} · {Math.round(c.confidence * 100)}% confident · {c.reason}
            </p>
            <p className="mt-1 flex gap-3 text-xs">
              <button
                type="button"
                disabled={busy !== null}
                onClick={() => decide(c.article.id, 'approve')}
                className="underline decoration-dotted underline-offset-2 hover:text-fg disabled:opacity-50"
              >
                {busy === c.article.id + 'approve' ? 'Approving…' : 'Approve — admit to pipeline'}
              </button>
              <button
                type="button"
                disabled={busy !== null}
                onClick={() => decide(c.article.id, 'reject')}
                className="underline decoration-dotted underline-offset-2 hover:text-fg disabled:opacity-50"
              >
                {busy === c.article.id + 'reject' ? 'Rejecting…' : 'Reject — mark irrelevant'}
              </button>
            </p>
          </li>
        ))}
      </ul>
    </div>
  );
}

export function NarrativeTopics({
  query,
  symbol,
  date,
  newsSource,
}: {
  query: {
    data: NarrativesResponse | undefined;
    isPending: boolean;
    isError: boolean;
    error: unknown;
    refetch: () => void;
  };
  symbol: string;
  date: string;
  newsSource: NewsSource;
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
          {data.articlesConsidered > data.articlesClustered && (
            <span>
              {' '}Newest {data.articlesClustered} of {data.articlesConsidered} clustered — the
              embedding budget ceiling leaves older ones out, stated here instead of silently.
            </span>
          )}
          {' '}Gate census over {data.articlesConsidered} evaluated candidate(s): relevant{' '}
          {data.relevantCount} · irrelevant {data.irrelevantCount} · uncertain {data.uncertainCount}.
          Only relevant articles enter threads, evidence, and embeddings — the rest never leave
          the raw cache.
          {data.expansionQueries > 0 && (
            <span>
              {' '}Thin evidence triggered bounded retrieval expansion: {data.expansionQueries}{' '}
              keyword queries found {data.expansionNew} new article(s), {data.expansionRelevant}{' '}
              relevant — cutoff-filtered and gated like everything else.
            </span>
          )}
        </p>
      </CardHeader>
      <CardContent className="space-y-2">
        <CandidateReview
          symbol={symbol}
          date={date}
          newsSource={newsSource}
          onDecided={() => query.refetch()}
        />
        {data.topics.length === 0 ? (
          <EmptySection
            title="No narrative threads"
            body={whyNoThreads(data.newsSource, data.articlesConsidered, fmtDate(data.asOfDate))}
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
                {t.relevanceRate !== null && t.relevanceRate !== undefined && (
                  <p className="mt-1 text-xs text-fg-dim">
                    Relevant {Math.round(t.relevanceRate * t.articleIds.length)}/{t.articleIds.length}
                    {t.topCategory ? ` · ${t.topCategory}` : ''} — semantic verdicts, verify against the articles
                  </p>
                )}
                {NON_ASCII.test(t.representativeTitle) && !t.brief && (
                  <ThreadGist
                    symbol={symbol}
                    date={date}
                    newsSource={newsSource}
                    articleIds={t.articleIds}
                  />
                )}
                {t.brief && (
                  <div className="mt-2">
                    <AiBriefBlock brief={t.brief} context="labels name shared vocabulary" />
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
