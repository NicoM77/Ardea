import type { UiPriorityLevel } from 'ui';
import type { ActionSummary, OverviewView, SourceStatus } from '../../core/api/client';
import { ACTION_PRIORITIES, num, priorityOf, type ActionPriority } from '../actions/action-format';

/**
 * The overview's numbers (task 3.8, master plan 11.4, mockup overview.html): the sentence that says
 * what most needs attention and the priority split. A reminder has no deadline and no owner (master
 * plan 17.5), so the sentence speaks of priority and exploitation only. Everything is computed from
 * the API's answers, so a number shown twice is one value.
 */

export interface Sentence {
  key: string;
  params: Record<string, string | number>;
}

/** Open actions per priority, from the overview's counts. */
export function openByPriority(overview: OverviewView): Record<ActionPriority, number> {
  const counts: Record<ActionPriority, number> = { critical: 0, high: 0, medium: 0, low: 0 };
  for (const row of overview.openByLevel) {
    const p = priorityOf(row.levelName);
    if (p !== 'none') counts[p] += num(row.actions);
  }
  return counts;
}

/**
 * The finding at the top of the page, in two parts: the lead (strong) and the detail.
 * Critical first; else high; else calm; else nothing open at all.
 */
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

/** Per project: one block per open action in its priority's colour, highest first. */
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

/** A warning's source, from the prefix of its key (`bacs-warnings:42`). */
export function warningSource(key: string, sources: readonly SourceStatus[]): string | null {
  const prefix = key.includes(':') ? key.slice(0, key.indexOf(':')) : null;
  return (prefix && sources.find((s) => s.key === prefix)?.name) || null;
}
