import type { StatusMetric, StatusWarning, WarningNotified } from '../../core/api/client';

export interface MetricGroup {
  readonly group: string;
  readonly rows: readonly MetricRow[];
}

export interface MetricRow {
  readonly name: string;
  readonly value: number;
  readonly unit: string | null;
}

const ORDER = ['worker', 'jobs', 'sources', 'deliveries', 'notify', 'ai', 'repositories'];

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

export function notifiedAt(
  notified: readonly WarningNotified[],
  warning: StatusWarning,
): string | null {
  const subject = warning.subject ?? null;
  return (
    notified.find((n) => n.code === warning.code && (n.subject ?? null) === subject)?.at ?? null
  );
}
