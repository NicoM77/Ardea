import type { StatusMetric, StatusWarning, WarningNotified } from '../../core/api/client';

export interface MetricGroup {
  readonly group: string;
  readonly label: string | null;
  readonly rows: readonly MetricRow[];
}

interface MetricRow {
  readonly name: string;
  readonly label: MetricLabel | null;
  readonly value: number;
  readonly unit: string | null;
}

interface MetricLabel {
  readonly subject: string | null;
  readonly key: string;
}

const ORDER = ['worker', 'jobs', 'sources', 'deliveries', 'notify', 'ai', 'repositories'];

const GROUP_LABELS: Readonly<Record<string, string>> = {
  worker: 'admin.operations.group.worker',
  jobs: 'admin.operations.group.jobs',
  sources: 'admin.operations.group.sources',
  deliveries: 'admin.operations.group.deliveries',
  notify: 'admin.operations.group.notify',
  ai: 'admin.operations.group.ai',
  repositories: 'admin.operations.group.repositories',
};

const MEASURE_LABELS: Readonly<Record<string, Readonly<Record<string, string>>>> = {
  worker: { alive: 'admin.operations.metric.workersAlive' },
  jobs: {
    pending: 'admin.operations.metric.jobPending',
    running: 'admin.operations.metric.jobRunning',
    failed: 'admin.operations.metric.jobFailed',
    dead: 'admin.operations.metric.jobDead',
    oldest_wait: 'admin.operations.metric.jobOldestWait',
  },
  sources: {
    failures: 'admin.operations.metric.sourceFailures',
    quarantined: 'admin.operations.metric.sourceQuarantined',
    lag: 'admin.operations.metric.sourceLag',
  },
  deliveries: {
    pending: 'admin.operations.metric.deliveryPending',
    sending: 'admin.operations.metric.deliverySending',
    sent: 'admin.operations.metric.deliverySent',
    failed: 'admin.operations.metric.deliveryFailed',
    dead: 'admin.operations.metric.deliveryDead',
    suppressed: 'admin.operations.metric.deliverySuppressed',
  },
  notify: { events_failed: 'admin.operations.metric.eventsFailed' },
  ai: {
    calls: 'admin.operations.metric.aiCalls',
    tokens_in: 'admin.operations.metric.aiTokensIn',
    tokens_out: 'admin.operations.metric.aiTokensOut',
    refused: 'admin.operations.metric.aiRefused',
  },
  repositories: {
    pending: 'admin.operations.metric.repositoryPending',
    ok: 'admin.operations.metric.repositoryOk',
    partial: 'admin.operations.metric.repositoryPartial',
    failing: 'admin.operations.metric.repositoryFailing',
  },
};

const WITH_SUBJECT = new Set(['jobs', 'sources', 'ai']);

export function metricLabel(group: string, name: string): MetricLabel | null {
  const measures = MEASURE_LABELS[group];
  if (!measures) return null;
  const cut = WITH_SUBJECT.has(group) ? name.lastIndexOf('.') : -1;
  const subject = cut > 0 ? name.slice(0, cut) : null;
  const key = measures[cut > 0 ? name.slice(cut + 1) : name];
  if (!key || (WITH_SUBJECT.has(group) && !subject)) return null;
  return { subject, key };
}

export function groupMetrics(metrics: readonly StatusMetric[]): MetricGroup[] {
  const groups = new Map<string, MetricRow[]>();
  for (const metric of metrics) {
    const rows = groups.get(metric.group) ?? [];
    rows.push({
      name: metric.name,
      label: metricLabel(metric.group, metric.name),
      value: Number(metric.value),
      unit: metric.unit ?? null,
    });
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
      label: GROUP_LABELS[group] ?? null,
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
