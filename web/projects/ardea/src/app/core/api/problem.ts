import { HttpErrorResponse } from '@angular/common/http';

export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  code?: string;
  errors?: Record<string, string[]>;
}

export interface ProblemMessages {
  codes?: Readonly<Record<string, string>>;
  statuses?: Readonly<Record<number, string>>;
  fallback: string;
}

export const COMMON_PROBLEM_KEYS = {
  offline: 'errors.offline',
  tooManyRequests: 'errors.tooManyRequests',
  server: 'errors.server',
} as const;

export function problemOf(error: unknown): ProblemDetails | null {
  if (!(error instanceof HttpErrorResponse)) return null;
  const body: unknown = error.error;
  if (body && typeof body === 'object') return body as ProblemDetails;
  return { status: error.status };
}

export function problemMessageKey(error: unknown, messages: ProblemMessages): string {
  if (!(error instanceof HttpErrorResponse)) return COMMON_PROBLEM_KEYS.server;
  const problem = problemOf(error);
  const code = problem?.code;
  if (code && messages.codes?.[code]) return messages.codes[code];
  const status = error.status;
  if (messages.statuses?.[status]) return messages.statuses[status];
  if (status === 0) return COMMON_PROBLEM_KEYS.offline;
  if (status === 429) return COMMON_PROBLEM_KEYS.tooManyRequests;
  if (status >= 500) return COMMON_PROBLEM_KEYS.server;
  return messages.fallback;
}
