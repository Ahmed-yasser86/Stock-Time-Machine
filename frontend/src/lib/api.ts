import type {
  Company,
  LiveQuote,
  MethodologyDoc,
  MovesResponse,
  NarrativesResponse,
  NewsSource,
  ProblemDetails,
  SimulationRequest,
  SimulationResponse,
  SnapshotResponse,
} from '../types';

export const API_BASE = import.meta.env.VITE_API_URL || 'http://localhost:5251';

export class ApiError extends Error {
  status: number;
  problem: ProblemDetails | null;
  constructor(message: string, status: number, problem: ProblemDetails | null) {
    super(message);
    this.status = status;
    this.problem = problem;
  }
}

async function request<T>(path: string, options?: RequestInit): Promise<T> {
  let resp: Response;
  try {
    resp = await fetch(`${API_BASE}${path}`, {
      headers: { Accept: 'application/json' },
      ...options,
    });
  } catch {
    throw new ApiError('The investigation service is unreachable. Check that the backend is running and try again.', 0, null);
  }

  if (!resp.ok) {
    let problem: ProblemDetails | null = null;
    try {
      problem = (await resp.json()) as ProblemDetails;
    } catch {
      /* non-JSON error body */
    }
    throw new ApiError(problem?.detail || resp.statusText || 'Request failed', resp.status, problem);
  }
  return (await resp.json()) as T;
}

export function apiErrorMessage(err: unknown, fallback: string): string {
  if (err instanceof ApiError && err.message) return err.message;
  if (err instanceof Error && err.message) return err.message;
  return fallback;
}

export const api = {
  health: () => request<{ status: string; time: string }>('/health'),
  companySearch: (q: string) =>
    request<Company[]>(`/api/timemachine/company-search?q=${encodeURIComponent(q)}`),
  snapshot: (symbol: string, date: string, newsSource?: NewsSource) =>
    request<SnapshotResponse>(
      `/api/timemachine/snapshot?symbol=${encodeURIComponent(symbol)}&date=${encodeURIComponent(date)}` +
        (newsSource ? `&newsSource=${encodeURIComponent(newsSource)}` : ''),
    ),
  quote: (symbol: string) =>
    request<LiveQuote>(`/api/timemachine/quote?symbol=${encodeURIComponent(symbol)}`),
  moves: (symbol: string, date: string, newsSource?: NewsSource) =>
    request<MovesResponse>(
      `/api/timemachine/moves?symbol=${encodeURIComponent(symbol)}&date=${encodeURIComponent(date)}` +
        (newsSource ? `&newsSource=${encodeURIComponent(newsSource)}` : ''),
    ),
  narratives: (symbol: string, date: string, newsSource?: NewsSource) =>
    request<NarrativesResponse>(
      `/api/timemachine/narratives?symbol=${encodeURIComponent(symbol)}&date=${encodeURIComponent(date)}` +
        (newsSource ? `&newsSource=${encodeURIComponent(newsSource)}` : ''),
    ),
  methodology: () => request<MethodologyDoc>('/api/timemachine/methodology'),
  runSimulation: (body: SimulationRequest) =>
    request<SimulationResponse>('/api/timemachine/simulation', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify(body),
    }),
};
