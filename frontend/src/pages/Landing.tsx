import { Link } from 'react-router-dom';
import { ArrowRight, Eye, FlaskConical, Scale } from 'lucide-react';
import { buttonVariants } from '../components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../components/ui/card';
import { Badge } from '../components/ui/badge';

const SAMPLE = { symbol: 'TSLA', date: '2020-01-15' };

export default function Landing() {
  return (
    <div className="space-y-12">
      {/* Value proposition */}
      <section className="space-y-6 pt-6 text-center" aria-labelledby="landing-title">
        <Badge variant="secondary">A pre-decision research instrument</Badge>
        <h1 id="landing-title" className="mx-auto max-w-3xl font-display text-4xl font-semibold tracking-tight text-balance sm:text-5xl">
          What could you have known <span className="text-temporal">then?</span>
        </h1>
        <p className="mx-auto max-w-2xl text-lg text-fg-muted">
          Before you act on a thesis today, see what investors actually knew at the last
          comparable moment in a stock&apos;s history. Not what we know now — what they
          knew <em>then</em>. Then discover what followed.
        </p>
        <div className="flex flex-wrap justify-center gap-3">
          <Link to="/investigate" className={buttonVariants({ size: 'lg' })}>
            Start an investigation <ArrowRight aria-hidden="true" />
          </Link>
          <Link to="/methodology" className={buttonVariants({ size: 'lg', variant: 'outline' })}>
            How the reconstruction works
          </Link>
        </div>
      </section>

      {/* Real example */}
      <section aria-label="Example investigation">
        <Card>
          <CardHeader>
            <CardTitle className="text-base">See it on a real moment</CardTitle>
            <CardDescription>
              Tesla in mid-January 2020 — before the year that redefined the company. Open the
              full reconstruction: prices, SEC filings, disclosures, and news available on that date only.
            </CardDescription>
          </CardHeader>
          <CardContent className="flex flex-wrap items-center gap-3">
            <Badge variant="outline" className="font-mono">TSLA</Badge>
            <Badge variant="outline">January 15, 2020</Badge>
            <Badge variant="outline">Cutoff 23:59 US/Eastern</Badge>
            <Link
              to={`/snapshot?symbol=${SAMPLE.symbol}&date=${SAMPLE.date}`}
              className={buttonVariants()}
            >
              Open this investigation <ArrowRight aria-hidden="true" />
            </Link>
            <Link
              to={`/snapshot?symbol=${SAMPLE.symbol}&date=${SAMPLE.date}&guided=1`}
              className={buttonVariants({ variant: 'outline' })}
            >
              Take the guided tour
            </Link>
          </CardContent>
        </Card>
      </section>

      {/* Journey strip — the investigation path at a glance */}
      <section aria-label="How an investigation flows">
        <ol className="flex flex-wrap items-center justify-center gap-1 text-sm">
          {[
            { label: 'Setup', to: '/investigate' },
            { label: 'Reconstruct', to: `/snapshot?symbol=${SAMPLE.symbol}&date=${SAMPLE.date}` },
            { label: 'Lens', to: `/moves?symbol=${SAMPLE.symbol}&date=${SAMPLE.date}` },
            { label: 'Compare', to: '/compare' },
          ].map((s, i, arr) => (
            <li key={s.label} className="flex items-center gap-1">
              {i > 0 && (
                <span aria-hidden="true" className="text-temporal">
                  →
                </span>
              )}
              <Link
                to={s.to}
                className="rounded-md border border-border bg-surface px-3 py-1.5 font-medium text-fg-muted hover:border-fg-dim hover:text-fg"
              >
                <span className="mr-1.5 font-mono text-xs text-temporal">{i + 1}</span>
                {s.label}
              </Link>
              {i === arr.length - 1 && (
                <span aria-hidden="true" className="text-temporal">
                  →
                </span>
              )}
            </li>
          ))}
          <li className="flex items-center gap-1">
            <span className="rounded-md border border-dashed border-border px-3 py-1.5 text-fg-dim">
              <span className="mr-1.5 font-mono text-xs">5</span>
              Conclude (yours)
            </span>
          </li>
        </ol>
      </section>

      {/* Dual positioning */}
      <section className="grid gap-4 md:grid-cols-3" aria-label="What this is for">
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <Scale className="size-4 text-primary" aria-hidden="true" /> Pre-decision diligence
            </CardTitle>
          </CardHeader>
          <CardContent className="text-sm text-fg-muted">
            About to take a position? Stress-test your thesis against what investors knew at
            comparable historical moments — before you act.
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <Eye className="size-4 text-primary" aria-hidden="true" /> Retrospective analysis
            </CardTitle>
          </CardHeader>
          <CardContent className="text-sm text-fg-muted">
            Trying to understand why a stock moved? Reconstruct the pre-event information
            environment and separate signal from noise.
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <FlaskConical className="size-4 text-primary" aria-hidden="true" /> Judgment training
            </CardTitle>
          </CardHeader>
          <CardContent className="text-sm text-fg-muted">
            Decide with only the information that existed then, then see what actually followed.
            Every investigation compounds your judgment.
          </CardContent>
        </Card>
      </section>

      <p className="text-center text-xs text-fg-dim">
        Stock Time Machine is a research instrument, not investment advice. Past reconstructions
        say nothing about future performance.
      </p>
    </div>
  );
}
