import { newsSourceLabel, type NewsSource } from '../types';

/**
 * Deterministic "why am I seeing this" copy for empty states. No AI: the
 * reasons are facts about source, cutoff, and counts the caller already has.
 * Each entry pairs the explanation with the concrete remedy.
 */
export function whyNoThreads(newsSource: NewsSource, articlesConsidered: number, asOfDate?: string): string {
  if (articlesConsidered === 0) {
    const scope = asOfDate ? ` at or before ${asOfDate}` : ' for this window';
    return (
      `No cached ${newsSourceLabel(newsSource)} articles exist${scope}, so there is ` +
      `nothing to cluster. Run a snapshot or moves investigation first — coverage warms the ` +
      `cache at zero extra cost.`
    );
  }
  return (
    `All ${articlesConsidered} cached article(s) were filtered before clustering. ` +
    `Try another news source — sources are never mixed, so each one shows only its own coverage.`
  );
}

export function whyNoMoves(): string {
  return (
    `Nothing in this window cleared the significance bar — an unusually calm market ` +
    `is itself information about the decision context. Try a more volatile window or date.`
  );
}

export function whyNoNews(newsSource: NewsSource, asOfDate: string): string {
  const sourceNote =
    newsSource === 'gdelt'
      ? 'GDELT Cloud covers March 2026 onward; older windows honestly return empty.'
      : newsSource === 'marketaux'
        ? 'MarketAux covers recent years only on the free tier.'
        : 'Alpha Vantage serves a trailing 7-day window per fetch.';
  return `No ${newsSourceLabel(newsSource)} news before ${asOfDate}. ${sourceNote} Absence of coverage does not mean absence of events.`;
}
