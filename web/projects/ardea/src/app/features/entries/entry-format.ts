import type { EntrySummary } from '../../core/api/client';
import type { UiPillTone } from 'ui';

/** The filters of the entries list that the API answers (GET /api/v1/entries: q, kind, changedSince). */
export interface EntryFilters {
  q: string;
  kind: '' | 'vulnerability' | 'warning';
  since: '' | 'day' | 'week' | 'month';
}

export const NO_FILTERS: EntryFilters = { q: '', kind: '', since: '' };

const SINCE_DAYS: Record<Exclude<EntryFilters['since'], ''>, number> = {
  day: 1,
  week: 7,
  month: 30,
};

/** Reads the filters from the address, accepting only values the list knows. */
export function filtersFrom(params: {
  q?: string | null;
  kind?: string | null;
  since?: string | null;
}): EntryFilters {
  const kind = params.kind === 'vulnerability' || params.kind === 'warning' ? params.kind : '';
  const since =
    params.since === 'day' || params.since === 'week' || params.since === 'month'
      ? params.since
      : '';
  return { q: (params.q ?? '').trim(), kind, since };
}

/** The query of the list request; empty filters are left out, `since` becomes a timestamp. */
export function listParams(
  filters: EntryFilters,
  cursor: string | undefined,
  now: Date,
): { q?: string; kind?: string; changedSince?: string; cursor?: string; limit: number } {
  return {
    ...(filters.q ? { q: filters.q } : {}),
    ...(filters.kind ? { kind: filters.kind } : {}),
    ...(filters.since
      ? {
          changedSince: new Date(
            now.getTime() - SINCE_DAYS[filters.since] * 86_400_000,
          ).toISOString(),
        }
      : {}),
    ...(cursor ? { cursor } : {}),
    limit: 50,
  };
}

/**
 * The rows the list shows: a first page replaces them, a further page (loaded with the previous
 * page's cursor) is appended. An entry that moved between pages appears once.
 */
export function mergePage(
  shown: readonly EntrySummary[],
  page: readonly EntrySummary[],
  isFirstPage: boolean,
): EntrySummary[] {
  if (isFirstPage) return [...page];
  const known = new Set(shown.map((entry) => entry.key));
  return [...shown, ...page.filter((entry) => !known.has(entry.key))];
}

/** The signals an entry carries, as pills: exploitation first, because it matters most. */
export function signalPills(entry: EntrySummary): { key: string; tone: UiPillTone }[] {
  const pills: { key: string; tone: UiPillTone }[] = [];
  if (entry.exploitation === 'active')
    pills.push({ key: 'entries.signals.exploited', tone: 'alert' });
  else if (entry.exploitation === 'poc')
    pills.push({ key: 'entries.signals.poc', tone: 'neutral' });
  if (entry.epssClass === 'high') pills.push({ key: 'entries.signals.epssHigh', tone: 'neutral' });
  if (entry.authorityWarning) pills.push({ key: 'entries.signals.authority', tone: 'neutral' });
  if (entry.fixAvailable) pills.push({ key: 'entries.signals.fix', tone: 'done' });
  return pills;
}

export const SEVERITY_LABELS: Record<string, string> = {
  critical: 'entries.severity.critical',
  high: 'entries.severity.high',
  medium: 'entries.severity.medium',
  low: 'entries.severity.low',
  none: 'entries.severity.none',
  unrated: 'entries.severity.unrated',
};

export const STATE_LABELS: Record<string, string> = {
  published: 'entries.state.published',
  disputed: 'entries.state.disputed',
  rejected: 'entries.state.rejected',
  merged: 'entries.state.merged',
};

export const KIND_LABELS: Record<string, string> = {
  vulnerability: 'entries.kind.vulnerability',
  warning: 'entries.kind.warning',
};

/** A CVSS score as the list prints it: one decimal, or nothing. */
export function score(value: number | string | null): string | null {
  if (value === null || value === '') return null;
  const number = typeof value === 'number' ? value : Number.parseFloat(value);
  return Number.isFinite(number) ? number.toFixed(1) : null;
}

/** The address of an entry's page; keys may hold a colon (`bacs-warnings:42`). */
export function entryLink(key: string): string[] {
  return ['/entries', key];
}
