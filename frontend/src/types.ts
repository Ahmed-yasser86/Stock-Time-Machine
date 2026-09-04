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

export type NewsSource = 'gdelt' | 'alphavantage' | 'marketaux';

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

export interface KeyMove {
  date: string;
  close: number;
  dailyReturnPct: number;
  zScore: number;
  volumeRatio: number;
  fiveDayMomentumPct: number;
  score: number;
  flags: string[];
  sentimentDirection: 'agree' | 'disagree' | 'neutral' | 'unknown' | string;
}

export interface MarketReaction {
  date: string;
  close: number;
}

export interface MoveFiling {
  accessionNumber: string;
  formType: string;
  filedAt: string;
  url: string;
}

export interface MoveNews {
  id: string;
  title: string;
  source: string;
  publishedAt: string;
  url: string;
  sentimentScore: number | null;
}

export interface SocialSignal {
  id: string;
  provider: string;
  community: string;
  title: string;
  excerpt: string;
  url: string;
  createdAt: string;
  score: number;
  commentCount: number;
  flair: string | null;
}

export interface ArrivalEntry {
  layer: string;
  firstSeen: string | null;
  state: 'observed' | 'silent' | string;
  lagHours: number | null;
  detail: string;
}

export interface MoveEvidence {
  filings: MoveFiling[];
  news: MoveNews[];
  social: SocialSignal[];
  reaction: MarketReaction[];
  unavailableLayers: string[];
  arrival: ArrivalEntry[];
}

export interface WindowSummary {
  tradingDays: number;
  cumulativeReturnPct: number;
  volatility: number;
  maxDrawdownPct: number;
  bestDay: string | null;
  bestDayReturnPct: number;
  worstDay: string | null;
  worstDayReturnPct: number;
  sufficientHistory: boolean;
}

export interface UncertaintyComponent {
  name: string;
  weight: number;
  value: number;
  detail: string;
}

export interface UncertaintyIndex {
  score: number;
  components: UncertaintyComponent[];
}

export interface ClusterBrief {
  summary: string;
  keyPoints: string[];
  model: string;
}

export interface TopicCluster {
  labelTerms: string[];
  articleIds: string[];
  representativeTitle: string;
  spanStart: string | null;
  spanEnd: string | null;
  brief: ClusterBrief | null;
}

export interface CompareBriefResponse {
  symbols: string[];
  asOfDate: string;
  newsSource: NewsSource;
  terms: string[];
  brief: ClusterBrief | null;
}

export interface CopilotBriefResponse {
  symbol: string;
  asOfDate: string;
  action: string;
  brief: ClusterBrief | null;
}

export interface NoteIssue {
  ref: string;
  verdict: string;
  detail: string;
}

export interface ReviewResponse {
  symbol: string;
  asOfDate: string;
  issues: NoteIssue[];
}

export interface NarrativesResponse {
  company: CompanySummary;
  asOfDate: string;
  newsSource: NewsSource;
  articlesConsidered: number;
  clusteringMethod: string;
  topics: TopicCluster[];
}

export interface MovesResponse {
  company: CompanySummary;
  decisionDate: string;
  newsSource: NewsSource;
  summary: WindowSummary;
  keyMoves: KeyMove[];
  evidenceByDate: Record<string, MoveEvidence>;
  windowPrices: PricePoint[];
  uncertainty: UncertaintyIndex;
  regimes: Record<string, 'calm' | 'normal' | 'tense' | 'warming' | string>;
}

export const NEWS_COVERAGE_DISCLAIMER =
  'News coverage is best-effort and may be incomplete. Absence of coverage does not mean absence of events.';

export const SIMULATION_DISCLAIMER =
  'This simulation uses raw historical closing prices. Stock splits and dividend payments are not accounted for in this calculation. This is not investment advice.';

export function newsSourceLabel(source: NewsSource | string): string {
  if (source === 'alphavantage') return 'Alpha Vantage';
  if (source === 'marketaux') return 'MarketAux';
  return 'GDELT';
}
