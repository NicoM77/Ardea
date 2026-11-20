import type { UiPriorityLevel } from 'ui';
import type { ActionSummary, OverviewView, SourceStatus } from '../../core/api/client';
import { ACTION_PRIORITIES, num, priorityOf, type ActionPriority } from '../actions/action-format';

export interface Sentence {
  key: string;
  params: Record<string, string | number>;
}

export function openByPriority(overview: OverviewView): Record<ActionPriority, number> {
  const counts: Record<ActionPriority, number> = { critical: 0, high: 0, medium: 0, low: 0 };
  for (const row of overview.openByLevel) {
    const p = priorityOf(row.levelName);
    if (p !== 'none') counts[p] += num(row.actions);
  }
  return counts;
}

export function overviewFinding(
  overview: OverviewView,
  open: readonly ActionSummary[],
): { lead: Sentence; detail: Sentence | null } {
  const counts = openByPriority(overview);
  const total = ACTION_PRIORITIES.reduce((n, p) => n + counts[p], 0);
  if (counts.critical > 0) {
    const critical = open.filter((a) => a.levelName === 'critical');
    return {
      lead: { key: 'overview.finding.critical', params: { count: counts.critical } },
      detail: {
        key: 'overview.finding.criticalDetail',
        params: {
          exploited: critical.filter((a) => a.exploitation === 'active').length,
          fix: critical.filter((a) => a.fixAvailable).length,
        },
      },
    };
  }
  if (counts.high > 0)
    return {
      lead: { key: 'overview.finding.noCritical', params: {} },
      detail: { key: 'overview.finding.highDetail', params: { count: counts.high } },
    };
  if (total > 0)
    return {
      lead: { key: 'overview.finding.calm', params: {} },
      detail: { key: 'overview.finding.calmDetail', params: {} },
    };
  return { lead: { key: 'overview.finding.nothing', params: {} }, detail: null };
}

export function projectBlocks(
  overview: OverviewView,
  open: readonly ActionSummary[],
): { id: string; name: string; count: number; priorities: UiPriorityLevel[] }[] {
  const order: UiPriorityLevel[] = ['critical', 'high', 'medium', 'low', 'none'];
  return overview.projects.map((p) => ({
    id: p.project.id,
    name: p.project.name,
    count: num(p.actions),
    priorities: open
      .filter((a) => a.project.id === p.project.id)
      .map((a) => priorityOf(a.levelName))
      .sort((a, b) => order.indexOf(a) - order.indexOf(b)),
  }));
}

export function warningSource(key: string, sources: readonly SourceStatus[]): string | null {
  const prefix = key.includes(':') ? key.slice(0, key.indexOf(':')) : null;
  return (prefix && sources.find((s) => s.key === prefix)?.name) || null;
}

export function sourceSummary(
  sources: readonly SourceStatus[],
): (Sentence & { attention: SourceStatus[] }) | null {
  if (!sources.length) return null;
  const count = sources.length;
  const attention = sources.filter((s) => !s.enabled || s.health !== 'healthy');
  if (!attention.length)
    return { key: 'overview.sources.healthy', params: { count }, attention: [] };
  if (attention.length === count && attention.every((s) => !s.enabled))
    return { key: 'overview.sources.paused', params: { count }, attention: [] };
  return {
    key: 'overview.sources.some',
    params: { count, healthy: count - attention.length },
    attention,
  };
}
