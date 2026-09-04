import type { ClusterBrief } from '../types';
import { Badge } from './ui/badge';

/** Consistent generated-content voice: outline badge with model, generated +
 * non-deterministic disclaimer, summary + cited key points. Used by every AI
 * surface so generation is recognizable at a glance (and in print). */
export function AiBriefBlock({ brief, context }: { brief: ClusterBrief; context: string }) {
  return (
    <div className="ai-brief space-y-1 rounded-md bg-canvas p-2">
      <p className="flex flex-wrap items-center gap-2 text-xs">
        <Badge variant="outline">AI brief · {brief.model}</Badge>
        <span className="text-fg-dim">{context} — generated, non-deterministic, verify against the evidence</span>
      </p>
      <p className="text-sm">{brief.summary}</p>
      {brief.keyPoints.length > 0 && (
        <ul className="list-disc space-y-0.5 pl-5 text-sm">
          {brief.keyPoints.map((k, j) => (
            <li key={j}>{k}</li>
          ))}
        </ul>
      )}
    </div>
  );
}
