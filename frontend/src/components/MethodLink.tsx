import { Link } from 'react-router-dom';

/** Contextual trust link: "why am I seeing this" from any analytical surface
 * to the exact methodology section that governs it. */
export function MethodLink({ anchor, label = 'Why this?' }: { anchor: string; label?: string }) {
  return (
    <Link
      to={`/methodology#${anchor}`}
      className="text-xs text-fg-dim underline decoration-dotted underline-offset-2 hover:text-fg"
    >
      {label}
    </Link>
  );
}
