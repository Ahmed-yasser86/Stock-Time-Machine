export interface Company {
  symbol: string;
  name: string;
  cik: string;
  exchange: string;
  sector: string;
  industry: string;
}

export interface PricePoint {
  date: string;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
}

export interface Filing {
  accessionNumber: string;
  formType: string;
  filedAt: string;
  periodOfReport: string;
  url: string;
  summary: string;
}

export interface Disclosure {
  accessionNumber: string;
  formType: string;
  filedAt: string;
  url: string;
  title: string;
}

export interface NewsItem {
  title: string;
  source: string;
  publishedAt: string;
  url: string;
}

export interface PriceQuote {
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
  asOf: string;
}

export interface LiveQuote {
  currentPrice: number;
  change: number;
  percentChange: number;
  high: number;
  low: number;
  previousClose: number;
  asOfUtc: string;
  source: string;
}

export interface Outcome {
  price: number | null;
  prices: PricePoint[];
  filings: Filing[];
  liveQuote: LiveQuote | null;
}

export interface CompanySummary {
  symbol: string;
  name: string;
  cik: string;
  exchange: string;
  sector: string;
}

export type NewsSource = 'gdelt' | 'alphavantage';

export interface SnapshotResponse {
  company: CompanySummary;
  snapshotDate: string;
  cutoffUtc: string;
  price: PriceQuote;
  recentPrices: PricePoint[];
  filings: Filing[];
  corporateDisclosures: Disclosure[];
  news: NewsItem[];
  newsSource: NewsSource;
  outcome: Outcome;
  warnings: string[];
}

export interface SimulationRequest {
  symbol: string;
  entryDate: string;
  amount: number;
  exitDate?: string;
}

export interface SimulationResponse {
  entryPrice: number;
  sharesPurchased: number;
  exitPrice: number | null;
  finalValue: number;
  returnPercentage: number;
  investmentAmount: number;
  entryDate: string;
  exitDate?: string;
  disclaimer: string;
}

export interface MethodologySection {
  heading: string;
  body: string;
}

export interface MethodologyDoc {
  title: string;
  intro: string;
  sections: MethodologySection[];
}

export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  traceId?: string;
}

export const NEWS_COVERAGE_DISCLAIMER =
  'News coverage is best-effort and may be incomplete. Absence of coverage does not mean absence of events.';

export const SIMULATION_DISCLAIMER =
  'This simulation uses raw historical closing prices. Stock splits and dividend payments are not accounted for in this calculation. This is not investment advice.';

export function newsSourceLabel(source: NewsSource | string): string {
  return source === 'alphavantage' ? 'Alpha Vantage' : 'GDELT';
}
