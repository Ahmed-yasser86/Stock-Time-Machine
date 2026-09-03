import { format } from 'date-fns';

const MONTHS = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
];

function toDate(value: string | null | undefined): Date | null {
  if (!value) return null;
  const trimmed = value.trim();
  // Date-only values ("2026-08-03") have no timezone; anchor at local noon so
  // they render as the same calendar day in every timezone (UTC-12..UTC+12).
  // Full timestamps keep their instant and render in the viewer's local zone.
  const d = new Date(/^\d{4}-\d{2}-\d{2}$/.test(trimmed) ? `${trimmed}T12:00:00` : trimmed);
  return Number.isNaN(d.getTime()) ? null : d;
}

function toNum(value: number | null | undefined): number | null {
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
}

/** Money, null-safe. Never renders $0.00 for missing data — renders "—". */
export function fmtMoney(value: number | null | undefined): string {
  const v = toNum(value);
  if (v === null) return '—';
  return `$${v.toFixed(2)}`;
}

/** Signed money for gains/losses: +$12.30 / −$4.10 (text sign, never color-only). */
export function fmtSignedMoney(value: number | null | undefined): string {
  const v = toNum(value);
  if (v === null) return '—';
  const sign = v > 0 ? '+' : v < 0 ? '−' : '';
  return `${sign}$${Math.abs(v).toFixed(2)}`;
}

/** Signed percent: +5.25% / −3.10% / — */
export function fmtPct(value: number | null | undefined): string {
  const v = toNum(value);
  if (v === null) return '—';
  const sign = v > 0 ? '+' : v < 0 ? '−' : '';
  return `${sign}${Math.abs(v).toFixed(2)}%`;
}

/** Full human-readable date: "January 15, 2020". Never a bare numeric string. */
export function fmtDate(value: string | null | undefined): string {
  const d = toDate(value);
  if (!d) return '—';
  try {
    return format(d, 'MMMM d, yyyy');
  } catch {
    return '—';
  }
}

/** Date + time in true UTC: "January 15, 2020, 04:59 UTC". */
export function fmtDateTimeUtc(value: string | null | undefined): string {
  const d = toDate(value);
  if (!d) return '—';
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${MONTHS[d.getUTCMonth()]} ${d.getUTCDate()}, ${d.getUTCFullYear()}, ${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())} UTC`;
}

/** Compact axis date: "Jan 15". */
export function fmtDateShort(value: string | null | undefined): string {
  const d = toDate(value);
  if (!d) return '';
  try {
    return format(d, 'MMM d');
  } catch {
    return '';
  }
}

export function fmtVolume(value: number | null | undefined): string {
  const v = toNum(value);
  if (v === null) return '—';
  if (v >= 1_000_000_000) return `${(v / 1_000_000_000).toFixed(2)}B`;
  if (v >= 1_000_000) return `${(v / 1_000_000).toFixed(1)}M`;
  if (v >= 1_000) return `${(v / 1_000).toFixed(1)}K`;
  return Math.round(v).toString();
}

/** Today's date as yyyy-MM-dd (local) for <input type="date" max>. */
export function todayLocal(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

/** Gain/loss/neutral classification for styling + non-color indicators. */
export function direction(value: number | null | undefined): 'gain' | 'loss' | 'flat' {
  const v = toNum(value);
  if (v === null || v === 0) return 'flat';
  return v > 0 ? 'gain' : 'loss';
}
