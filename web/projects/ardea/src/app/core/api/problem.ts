import { HttpErrorResponse } from '@angular/common/http';

/** RFC 9457 problem details as the API sends them; `code` is a stable, machine-readable reason. */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  code?: string;
  errors?: Record<string, string[]>;
}

/** The error sentence keys a form knows, by problem `code` and by HTTP status. */
export interface ProblemMessages {
  codes?: Readonly<Record<string, string>>;
  statuses?: Readonly<Record<number, string>>;
  /** For every other failure the server answered. */
  fallback: string;
}

/** Keys every form shares when the server is unreachable or overloaded. */
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

/**
 * Picks the translation key for a failed request: a known `code` first, then a known status,
 * then the shared answers for "no connection", "too many attempts" and "server error", and the
 * form's own fallback for anything else. The server's English `title` is never shown, because the
 * interface speaks the user's language.
 */
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
