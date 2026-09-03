import { Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { ArrowLeft } from 'lucide-react';
import { api } from '../lib/api';
import { buttonVariants } from '../components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '../components/ui/card';
import { Skeleton } from '../components/ui/skeleton';
import { ErrorState } from '../components/StateBlocks';

export default function Methodology() {
  const doc = useQuery({ queryKey: ['methodology'], queryFn: api.methodology, staleTime: Infinity });

  if (doc.isPending) {
    return (
      <div className="mx-auto max-w-3xl space-y-4" aria-busy="true" aria-label="Loading methodology">
        <Skeleton className="h-10 w-2/3" />
        <Skeleton className="h-20 w-full" />
        <Skeleton className="h-64 w-full" />
      </div>
    );
  }

  if (doc.isError) {
    return (
      <div className="mx-auto max-w-3xl">
        <ErrorState error={doc.error} fallback="The methodology could not be loaded." onRetry={() => doc.refetch()} backTo="/" />
      </div>
    );
  }

  const { title, intro, sections } = doc.data;

  return (
    <div className="mx-auto max-w-3xl space-y-6">
      <div className="space-y-2">
        <h1 className="font-display text-3xl font-semibold tracking-tight">{title}</h1>
        <p className="text-fg-muted">{intro}</p>
      </div>

      {sections.length > 0 && (
        <nav aria-label="Methodology sections" className="flex flex-wrap gap-2">
          {sections.map((s) => (
            <a
              key={s.heading}
              href={`#${s.heading.toLowerCase().replace(/[^a-z0-9]+/g, '-')}`}
              className="rounded-full border border-border bg-surface px-3 py-1 text-sm text-fg-muted hover:border-primary hover:text-fg"
            >
              {s.heading}
            </a>
          ))}
        </nav>
      )}

      {sections.length === 0 ? (
        <p className="text-sm text-fg-muted">No methodology sections are available right now.</p>
      ) : (
        <div className="space-y-4">
          {sections.map((s) => (
            <Card key={s.heading} id={s.heading.toLowerCase().replace(/[^a-z0-9]+/g, '-')} className="scroll-mt-20">
              <CardHeader>
                <CardTitle className="font-display text-lg">{s.heading}</CardTitle>
              </CardHeader>
              <CardContent className="text-sm leading-relaxed text-fg-muted">{s.body}</CardContent>
            </Card>
          ))}
        </div>
      )}

      <Link to="/investigate" className={buttonVariants({ variant: 'outline' })}>
        <ArrowLeft aria-hidden="true" /> Start an investigation
      </Link>
    </div>
  );
}
