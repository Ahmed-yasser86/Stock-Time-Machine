import type { NewsSource } from '../types';

export interface RecentInvestigation {
  symbol: string;
  date: string;
  newsSource: NewsSource;
  visitedAt: number;
}

const KEY = 'stm:recent-investigations';
const MAX = 8;

function read(): RecentInvestigation[] {
  try {
    const raw = localStorage.getItem(KEY);
    if (!raw) return [];
    const parsed = JSON.parse(raw) as RecentInvestigation[];
    return Array.isArray(parsed) ? parsed.filter((r) => r.symbol && r.date) : [];
  } catch {
    return [];
  }
}

/** Session investigation memory: newest first, deduped by symbol|date|source. Local only, never accounts. */
export function recordInvestigation(symbol: string, date: string, newsSource: NewsSource): void {
  try {
    const key = `${symbol.toUpperCase()}|${date}|${newsSource}`;
    const rest = read().filter((r) => `${r.symbol.toUpperCase()}|${r.date}|${r.newsSource}` !== key);
    localStorage.setItem(
      KEY,
      JSON.stringify(
        [{ symbol: symbol.toUpperCase(), date, newsSource, visitedAt: Date.now() }, ...rest].slice(0, MAX),
      ),
    );
  } catch {
    /* private-mode storage: memory simply doesn't persist */
  }
}

export function getRecentInvestigations(): RecentInvestigation[] {
  return read();
}
