import type { StatusMetric, StatusWarning, WarningNotified } from '../../core/api/client';

/** The operating figures of one group, e.g. everything about the sources, in a stable order. */
export interface MetricGroup {
  readonly group: string;
  readonly rows: readonly MetricRow[];
}

export interface MetricRow {
  readonly name: string;
  readonly value: number;
  readonly unit: string | null;
}

/** The groups the server reports, in the order an operator reads them; unknown ones follow alphabetically. */
const ORDER = ['worker', 'jobs', 'sources', 'deliveries', 'notify', 'ai', 'repositories'];

/**
 * Groups the figures of `GET /api/v1/instance/status` by their group. The server sends counts and times
 * only; values arrive as numbers or, for large ones, as numeric strings.
 */
export function groupMetrics(metrics: readonly StatusMetric[]): MetricGroup[] {
  const groups = new Map<string, MetricRow[]>();
  for (const metric of metrics) {
    const rows = groups.get(metric.group) ?? [];
    rows.push({ name: metric.name, value: Number(metric.value), unit: metric.unit ?? null });
    groups.set(metric.group, rows);
  }
  const rank = (group: string): number => {
    const index = ORDER.indexOf(group);
    return index === -1 ? ORDER.length : index;
  };
  return [...groups.entries()]
    .sort(([a], [b]) => rank(a) - rank(b) || a.localeCompare(b, 'en'))
    .map(([group, rows]) => ({
      group,
      rows: [...rows].sort((a, b) => a.name.localeCompare(b.name, 'en')),
    }));
}

/**
 * When the instance administrators were last mailed about this warning (ADR 043), or null while they were
 * not. Warnings are keyed by code and subject, as the server keys them.
 */
export function notifiedAt(
  notified: readonly WarningNotified[],
  warning: StatusWarning,
): string | null {
  const subject = warning.subject ?? null;
  return (
    notified.find((n) => n.code === warning.code && (n.subject ?? null) === subject)?.at ?? null
  );
}
