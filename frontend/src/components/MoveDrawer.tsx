import { useEffect } from 'react';
import { Link } from 'react-router-dom';
import { ArrowDownRight, ArrowUpRight, ExternalLink, Minus, X } from 'lucide-react';
import { direction, fmtDate, fmtDateTimeUtc, fmtMoney } from '../lib/format';
import { ReactionSparkline } from './ReactionSparkline';
import type { KeyMove, MoveEvidence, NewsSource } from '../types';
import { Badge } from './ui/badge';
import { buttonVariants } from './ui/button';
import { EmptySection } from './StateBlocks';

const FLAG_LABELS: Record<string, string> = {
  spike: 'Unusually large up day',
  plunge: 'Unusually large down day',
  'high-volume': 'Unusually heavy volume',
  breakout: 'Broke above recent range',
  breakdown: 'Broke below recent range',
};

function LayerNote({ text }: { text: string }) {
  return <p className="rounded-lg border border-dashed border-border p-3 text-sm text-fg-muted">{text}</p>;
}

/**
 * Move investigation drawer: one significant movement, its market facts, the
 * evidence available by that movement's own cutoff, and the market reaction
 * after it. Copy uses temporal language only — never causal claims.
 */
export function MoveDrawer({
  move,
  rank,
  evidence,
  symbol,
  newsSource,
  onClose,
}: {
  move: KeyMove;
  rank: number;
  evidence: MoveEvidence | undefined;
  symbol: string;
  newsSource: NewsSource;
  onClose: () => void;
}) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [onClose]);

  const dir = direction(move.dailyReturnPct);
  const ev = evidence;
  const social = ev?.social ?? [];

  return (
    <aside
      role="dialog"
      aria-modal="false"
      aria-label={`Key move ${rank} on ${fmtDate(move.date)}`}
      className="fixed inset-x-0 bottom-0 z-50 max-h-[85vh] overflow-y-auto rounded-t-2xl border-t-2 border-temporal bg-surface p-4 shadow-xl md:left-auto md:right-6 md:top-24 md:bottom-6 md:w-[26rem] md:rounded-2xl md:border"
    >
      <div className="flex items-start gap-2">
        <div>
          <p className="text-xs font-medium tracking-widest text-temporal uppercase">Key move #{rank}</p>
          <h2 className="font-display text-xl font-semibold">
            {fmtDate(move.date)} ·{' '}
            <span className="font-mono tabular">
              {move.dailyReturnPct > 0 ? '+' : ''}{move.dailyReturnPct.toFixed(2)}%
            </span>
          </h2>
        </div>
        <button
          type="button"
          onClick={onClose}
          aria-label="Close move details"
          className="ml-auto rounded-md p-1 text-fg-muted hover:bg-accent hover:text-fg"
        >
          <X className="size-5" aria-hidden="true" />
        </button>
      </div>

      <div className="mt-2 flex items-center gap-2 text-sm">
        {dir === 'gain' ? (
          <ArrowUpRight className="size-4 text-gain" aria-label="Up" />
        ) : dir === 'loss' ? (
          <ArrowDownRight className="size-4 text-loss" aria-label="Down" />
        ) : (
          <Minus className="size-4 text-fg-dim" aria-label="Flat" />
        )}
        <span className="font-mono tabular">Close {fmtMoney(move.close)}</span>
        <span className="font-mono tabular text-fg-muted">z {move.zScore.toFixed(2)}</span>
        <span className="font-mono tabular text-fg-muted">vol {move.volumeRatio.toFixed(1)}×</span>
      </div>
      {move.flags.length > 0 && (
        <div className="mt-2 flex flex-wrap gap-1">
          {move.flags.map((f) => (
            <Badge key={f} variant="secondary" title={FLAG_LABELS[f] ?? f}>{f}</Badge>
          ))}
        </div>
      )}
      <p className="mt-2 text-xs text-fg-muted" title="Whether scored news leaned the same way the price moved. A disagreement is a contrarian lens, never a prediction.">
        Narrative vs market:{' '}
        <strong className="text-fg">
          {move.sentimentDirection === 'agree' && 'agree ✓'}
          {move.sentimentDirection === 'disagree' && 'disagree — worth a look'}
          {move.sentimentDirection === 'neutral' && 'neutral'}
          {move.sentimentDirection === 'unknown' && 'unknown (too few scored articles)'}
        </strong>
      </p>

      <h3 className="mt-4 text-sm font-semibold">How information arrived</h3>
      {!ev || ev.arrival.length === 0 ? (
        <p className="mt-1 text-sm text-fg-muted">Arrival data unavailable for this movement.</p>
      ) : (
        <ol className="mt-1 space-y-1.5">
          {ev.arrival.map((a) => (
            <li key={a.layer} className="flex items-start gap-2 text-sm">
              <span
                aria-hidden="true"
                className={`mt-1.5 size-2 shrink-0 rounded-full ${
                  a.state === 'observed' ? 'bg-primary' : 'border border-fg-dim bg-transparent'
                }`}
              />
              <div>
                <p className="font-medium capitalize">
                  {a.layer}
                  <span className="sr-only">: {a.state === 'observed' ? 'observed' : 'no evidence'}</span>
                </p>
                {a.state === 'observed' && a.firstSeen ? (
                  <p className="text-xs text-fg-muted">
                    First seen {fmtDateTimeUtc(a.firstSeen)}
                    {a.lagHours !== null && a.lagHours > 0
                      ? ` (+${a.lagHours}h after earliest)`
                      : ' (earliest)'}
                    {a.detail ? ` — ${a.detail}` : ''}
                  </p>
                ) : (
                  <p className="text-xs text-fg-dim">
                    No evidence in this layer before the movement — unknown, not absent.
                  </p>
                )}
              </div>
            </li>
          ))}
        </ol>
      )}

      <h3 className="mt-4 text-sm font-semibold">Market reaction after this move</h3>
      {ev && ev.reaction.length > 0 ? (
        <>
          <ReactionSparkline reaction={ev.reaction} moveClose={move.close} moveDate={move.date} />
          <ul className="mt-1 space-y-1 text-sm">
            {ev.reaction.map((r) => (
              <li key={r.date} className="flex justify-between font-mono tabular">
                <span>{fmtDate(r.date)}</span>
                <span>{fmtMoney(r.close)}</span>
              </li>
            ))}
          </ul>
          <p className="mt-1 text-xs text-fg-dim">
            Recorded closes only — no benchmark exists, so no abnormal-return claim is made.
          </p>
        </>
      ) : (
        <p className="mt-1 text-sm text-fg-muted">No subsequent closes available.</p>
      )}

      <h3 className="mt-4 text-sm font-semibold">Regulatory evidence available by then</h3>
      {!ev || (ev.filings.length === 0 && !ev.unavailableLayers.includes('regulatory')) ? (
        <p className="mt-1 text-sm text-fg-muted">No SEC filings available before this movement.</p>
      ) : ev.unavailableLayers.includes('regulatory') && ev.filings.length === 0 ? (
        <div className="mt-1"><LayerNote text="Regulatory evidence unavailable for this movement." /></div>
      ) : (
        <ul className="mt-1 space-y-2">
          {ev.filings.map((f) => (
            <li key={f.accessionNumber} className="rounded-lg border border-border p-2 text-sm">
              <p className="flex flex-wrap items-center gap-2">
                <Badge variant="secondary" className="font-mono">{f.formType}</Badge>
                <span className="text-fg-muted">Filed {fmtDate(f.filedAt)}</span>
                <a href={f.url} target="_blank" rel="noreferrer" className="ml-auto inline-flex items-center gap-1 text-primary underline-offset-4 hover:underline">
                  SEC.gov <ExternalLink className="size-3" aria-hidden="true" />
                </a>
              </p>
            </li>
          ))}
        </ul>
      )}

      <h3 className="mt-4 text-sm font-semibold">News published before this movement</h3>
      {!ev || ev.news.length === 0 ? (
        <div className="mt-1">
          <LayerNote text="No historical news was found before this movement. This does not mean nothing happened." />
        </div>
      ) : (
        <ul className="mt-1 space-y-2">
          {ev.news.map((n, i) => (
            <li key={`${n.url}-${i}`} className="rounded-lg border border-border p-2 text-sm">
              <p>
                <a href={n.url} target="_blank" rel="noreferrer" className="font-medium underline-offset-4 hover:underline">
                  {n.title}
                </a>
              </p>
              <p className="mt-1 text-xs text-fg-muted">
                {n.source} · Published {fmtDate(n.publishedAt)}
                {n.sentimentScore !== null && n.sentimentScore !== undefined && (
                  <span className="ml-1 rounded-full bg-accent px-1.5 py-0.5 font-mono tabular" title="Entity sentiment score from the provider, -1 to +1">
                    {n.sentimentScore > 0 ? '+' : ''}{n.sentimentScore.toFixed(2)}
                  </span>
                )}
              </p>
            </li>
          ))}
        </ul>
      )}

      <h3 className="mt-4 text-sm font-semibold">Retail discussion around this movement</h3>
      {!ev || (ev.social.length === 0 && !ev.unavailableLayers.includes('social')) ? (
        <div className="mt-1">
          <LayerNote text="No retail discussion found around this movement in covered communities." />
        </div>
      ) : ev.unavailableLayers.includes('social') && ev.social.length === 0 ? (
        <div className="mt-1"><LayerNote text="Retail discussion unavailable for this movement." /></div>
      ) : (
        <ul className="mt-1 space-y-2">
          {social.map((s) => (
            <li key={s.id} className="rounded-lg border border-border p-2 text-sm">
              <p>
                {s.url ? (
                  <a href={s.url} target="_blank" rel="noreferrer" className="font-medium underline-offset-4 hover:underline">
                    {s.title}
                  </a>
                ) : (
                  <span className="font-medium">{s.title}</span>
                )}
              </p>
              {s.excerpt && <p className="mt-1 text-fg-muted">{s.excerpt}</p>}
              <p className="mt-1 text-xs text-fg-muted">
                {s.provider} · {s.community} · {fmtDate(s.createdAt)}
                {s.flair ? ` · Flair: ${s.flair}` : ''} · ▲ {s.score} · {s.commentCount} comments
              </p>
            </li>
          ))}
        </ul>
      )}

      <div className="mt-4 flex flex-wrap gap-2">
        <Link
          to={`/snapshot?symbol=${encodeURIComponent(symbol)}&date=${move.date}&newsSource=${encodeURIComponent(newsSource)}`}
          className={buttonVariants({ size: 'sm' })}
        >
          Open full dossier at {fmtDate(move.date)}
        </Link>
      </div>
      <p className="mt-3 text-xs text-fg-dim">
        Evidence coincided with this movement; proximity in time is never presented as causation.
        Score {move.score.toFixed(3)} of 1.00 by the published weighting.
      </p>
    </aside>
  );
}

export function MoveDrawerEmpty() {
  return (
    <EmptySection
      title="Select a movement"
      body="Choose a numbered key move on the timeline or list to investigate the evidence available at that moment."
    />
  );
}
