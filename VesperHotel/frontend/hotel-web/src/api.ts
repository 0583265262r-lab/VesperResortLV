let accessToken: string | null = null;
let refreshing: Promise<string | null> | null = null;

export class ApiError extends Error {
  status: number;
  code: string;
  constructor(message: string, status: number, code: string) {
    super(message); this.status = status; this.code = code;
  }
}

export function setAccessToken(token: string | null) { accessToken = token; }

export async function api<T>(path: string, options: RequestInit = {}, retry = true): Promise<T> {
  const headers = new Headers(options.headers);
  if (options.body) headers.set('Content-Type', 'application/json');
  if (accessToken) headers.set('Authorization', `Bearer ${accessToken}`);
  const response = await fetch(`/api${path}`, { ...options, headers, credentials: 'include' });
  if (response.status === 401 && retry && !path.startsWith('/auth/')) {
    if (!refreshing) refreshing = refreshToken();
    const token = await refreshing;
    refreshing = null;
    if (token) return api<T>(path, options, false);
  }
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    const errors = problem.errors ? Object.values(problem.errors).flat().join(' ') : '';
    throw new ApiError(problem.message || errors || problem.title || 'Something went wrong. Please try again.', response.status, problem.code || 'REQUEST_FAILED');
  }
  if (response.status === 204) return undefined as T;
  const text = await response.text();
  return text ? JSON.parse(text) as T : undefined as T;
}

async function refreshToken() {
  try {
    const response = await api<{ accessToken: string }>('/auth/refresh', { method: 'POST' }, false);
    setAccessToken(response.accessToken); return response.accessToken;
  } catch { setAccessToken(null); return null; }
}

export function send<T>(path: string, body?: unknown, method = 'POST', headers?: HeadersInit) {
  return api<T>(path, { method, ...(body === undefined ? {} : { body: JSON.stringify(body) }), headers });
}
export const money = (value: number, currency = 'USD') => new Intl.NumberFormat('en-US', { style: 'currency', currency, maximumFractionDigits: value % 1 ? 2 : 0 }).format(value);
export const hotelDate = (value: string, withTime = false) => new Date(value).toLocaleString('en-US', { timeZone: 'America/Los_Angeles', month: 'short', day: 'numeric', year: 'numeric', ...(withTime ? { hour: 'numeric', minute: '2-digit' } : {}) });
export const errorMessage = (error: unknown) => error instanceof Error ? error.message : 'Unable to complete this request.';
export function upcomingDate(offset: number) { const date = new Date(); date.setDate(date.getDate() + offset); return date.toISOString().slice(0, 10); }

// Interpret wall-clock form input in the hotel's timezone, including daylight saving.
export function vegasToUtc(value: string) {
  const target = new Date(`${value}:00Z`);
  let result = new Date(target);
  for (let i = 0; i < 3; i++) {
    const parts = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Los_Angeles', year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23' }).formatToParts(result);
    const p = Object.fromEntries(parts.map(part => [part.type, part.value]));
    const local = Date.UTC(+p.year, +p.month - 1, +p.day, +p.hour, +p.minute, +p.second);
    result = new Date(result.getTime() + target.getTime() - local);
  }
  return result.toISOString();
}
export function utcToVegasInput(value: string) {
  const parts = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Los_Angeles', year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).formatToParts(new Date(value));
  const p = Object.fromEntries(parts.map(part => [part.type, part.value]));
  return `${p.year}-${p.month}-${p.day}T${p.hour}:${p.minute}`;
}
