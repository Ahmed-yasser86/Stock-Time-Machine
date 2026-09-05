import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { AlertTriangle, Check, FileSearch, Minus, RotateCcw, X } from 'lucide-react';
import { Alert, AlertDescription, AlertTitle } from './ui/alert';
import { Button, buttonVariants } from './ui/button';
import { Skeleton } from './ui/skeleton';
import { Card, CardContent } from './ui/card';
import { apiErrorMessage } from '../lib/api';

export function LoadingDossier() {
  return (
    <div className="space-y-4" aria-busy="true" aria-label="Reconstructing historical snapshot">
      <Skeleton className="h-24 w-full" />
      <div className="grid gap-4 md:grid-cols-4">
        <Skeleton className="h-28" />
        <Skeleton className="h-28" />
        <Skeleton className="h-28" />
        <Skeleton className="h-28" />
      </div>
      <Skeleton className="h-64 w-full" />
    </div>
  );
}

export interface StageEvent {
  stage: string;
  state: 'started' | 'complete' | 'partial' | 'failed' | 'skipped' | 'queued' | string;
  detail?: string | null;
  count?: number | null;
}

export const RECONSTRUCTION_STAGES: { key: string; label: string }[] = [
  { key: 'company', label: 'Identifying company' },
  { key: 'prices', label: 'Retrieving historical market data' },
  { key: 'boundary', label: 'Establishing temporal boundary' },
  { key: 'filings', label: 'Evaluating SEC filings' },
  { key: 'disclosures', label: 'Evaluating corporate disclosures' },
  { key: 'news', label: 'Searching for historical news and events' },
  { key: 'outcome', label: 'Evaluating subsequent outcomes' },
  { key: 'assembly', label: 'Assembling snapshot' },
];

/**
 * Live reconstruction progress (US-06). Every row reflects a real pipeline
 * stage streamed from the backend: queued → started → complete / failed /
 * skipped. A failed step shows its failure — never a success mark.
 */
export function ReconstructionProgress({
  stages,
  defs = RECONSTRUCTION_STAGES,
  title = 'Reconstructing the information environment…',
  footnote = 'Real data is being assembled from Alpha Vantage, SEC EDGAR, and your selected news source.',
}: {
  stages: StageEvent[];
  defs?: { key: string; label: string }[];
  title?: string;
  footnote?: string;
}) {
  const byKey = new Map(stages.map((s) => [s.stage, s]));
  return (
    <Card>
      <CardContent className="space-y-3 pt-6">
        <p className="text-sm font-medium">{title}</p>
        <ol className="space-y-2" aria-live="polite">
          {defs.map(({ key, label }) => {
            const state = byKey.get(key)?.state ?? 'queued';
            const detail = byKey.get(key)?.detail;
            return (
              <li key={key} className="flex items-center gap-3 text-sm text-fg-muted">
                {state === 'complete' && <Check className="size-4 shrink-0 text-gain" aria-label="Complete" />}
                {state === 'failed' && <X className="size-4 shrink-0 text-loss" aria-label="Failed" />}
                {state === 'skipped' && <Minus className="size-4 shrink-0 text-fg-dim" aria-label="Skipped" />}
                {(state === 'started' || state === 'queued') && (
                  <span
                    className={`inline-block size-2 shrink-0 rounded-full ${state === 'started' ? 'animate-pulse bg-temporal' : 'bg-border'}`}
                    aria-label={state === 'started' ? 'In progress' : 'Queued'}
                  />
                )}
                <span>
                  {label}
                  {detail && state !== 'queued' && <span className="text-fg-dim"> — {detail}</span>}
                  {state === 'failed' && <span className="text-loss"> — unavailable</span>}
                  {state === 'skipped' && <span className="text-fg-dim"> — not requested</span>}
                </span>
              </li>
            );
          })}
        </ol>
        <p className="text-xs text-fg-dim">{footnote}</p>
      </CardContent>
    </Card>
  );
}

export function ErrorState({
  error,
  fallback,
  onRetry,
  backTo,
}: {
  error: unknown;
  fallback: string;
  onRetry?: () => void;
  backTo?: string;
}) {
  return (
    <Alert variant="destructive">
      <AlertTriangle />
      <AlertTitle>Something went wrong</AlertTitle>
      <AlertDescription className="space-y-3">
        <p>{apiErrorMessage(error, fallback)}</p>
        <div className="flex flex-wrap gap-2">
          {onRetry && (
            <Button variant="outline" size="sm" onClick={onRetry}>
              <RotateCcw /> Try again
            </Button>
          )}
          {backTo && (
            <Link to={backTo} className={buttonVariants({ variant: 'outline', size: 'sm' })}>
              Start a new investigation
            </Link>
          )}
        </div>
      </AlertDescription>
    </Alert>
  );
}

export function EmptySection({
  title,
  body,
  action,
}: {
  title: string;
  body: string;
  action?: ReactNode;
}) {
  return (
    <div className="flex flex-col items-start gap-2 rounded-lg border border-dashed border-border p-6">
      <p className="flex items-center gap-2 text-sm font-medium">
        <FileSearch className="size-4 text-fg-dim" aria-hidden="true" /> {title}
      </p>
      <p className="text-sm text-fg-muted">{body}</p>
      {action}
    </div>
  );
}
