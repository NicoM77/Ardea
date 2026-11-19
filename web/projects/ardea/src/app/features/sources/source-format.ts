import type { SourceStatus } from '../../core/api/client';

export const HEALTH_LABELS: Record<string, string> = {
  healthy: 'sources.health.healthy',
  degraded: 'sources.health.degraded',
  failing: 'sources.health.failing',
  disabled: 'sources.health.disabled',
};

export const OPERATOR_KIND_LABELS: Record<string, string> = {
  authority: 'sources.operatorKind.authority',
  mandated: 'sources.operatorKind.mandated',
  recognized: 'sources.operatorKind.recognized',
  vendor: 'sources.operatorKind.vendor',
  community: 'sources.operatorKind.community',
};

/** The order operator kinds are offered in: by weight of the operator (master plan 5.6). */
const KIND_ORDER = ['authority', 'mandated', 'recognized', 'vendor', 'community'];

/** int64 and int32 values arrive as number or string (OpenAPI 3.1 from .NET). */
export function count(value: number | string): number {
  const number = typeof value === 'number' ? value : Number.parseInt(value, 10);
  return Number.isFinite(number) ? number : 0;
}

export interface SourceFilters {
  operatorKind: string;
  operator: string;
}

/** The operator kinds and operators present, for the filters; each offered once. */
export function filterOptions(sources: readonly SourceStatus[]): {
  kinds: string[];
  operators: string[];
} {
  const kinds = [...new Set(sources.map((s) => s.operatorKind))].sort(
    (a, b) => (KIND_ORDER.indexOf(a) + 1 || 99) - (KIND_ORDER.indexOf(b) + 1 || 99),
  );
  const operators = [...new Set(sources.map((s) => s.operator))].sort((a, b) => a.localeCompare(b));
  return { kinds, operators };
}

/** The sources a filter leaves, with the ones that need attention (failing, then degraded) first. */
export function filterSources(
  sources: readonly SourceStatus[],
  filters: SourceFilters,
): SourceStatus[] {
  const weight: Record<string, number> = { failing: 0, degraded: 1, healthy: 2, disabled: 3 };
  return sources
    .filter((s) => !filters.operatorKind || s.operatorKind === filters.operatorKind)
    .filter((s) => !filters.operator || s.operator === filters.operator)
    .sort(
      (a, b) => (weight[a.health] ?? 2) - (weight[b.health] ?? 2) || a.name.localeCompare(b.name),
    );
}

/** How many sources are in each health state, for the summary line. */
export function healthCounts(
  sources: readonly SourceStatus[],
): Record<'healthy' | 'degraded' | 'failing' | 'disabled', number> {
  const counts = { healthy: 0, degraded: 0, failing: 0, disabled: 0 };
  for (const source of sources) {
    if (source.health in counts) counts[source.health as keyof typeof counts]++;
  }
  return counts;
}

/** An interval in minutes as the page prints it: minutes below two hours, hours below two days, then days. */
export function interval(minutes: number): { key: string; value: number } {
  if (minutes < 120) return { key: 'sources.every.minutes', value: minutes };
  if (minutes < 2880) return { key: 'sources.every.hours', value: Math.round(minutes / 60) };
  return { key: 'sources.every.days', value: Math.round(minutes / 1440) };
}
