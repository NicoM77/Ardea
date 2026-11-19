import type { UiPriorityLevel } from 'ui';
import type { ActionSummary, OverviewView, SourceStatus } from '../../core/api/client';
import {
  ACTION_PRIORITIES,
  clockStart,
  num,
  priorityOf,
  type ActionPriority,
} from '../actions/action-format';

/**
 * The overview's numbers (task 3.8, master plan 11.4, mockup overview.html): the sentence that says
 * what most needs attention, the deadline runway and the priority split. Everything is computed
 * from the API's answers, so a number shown twice is one value.
 */

const DAY = 86_400_000;

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

/** The open actions with a deadline, soonest first. */
export function byDeadline(rows: readonly ActionSummary[]): ActionSummary[] {
  return rows
    .filter((a) => a.dueAt && a.status !== 'closed')
    .sort((a, b) => new Date(a.dueAt!).getTime() - new Date(b.dueAt!).getTime());
}

function sameDay(a: Date, b: Date): boolean {
  return (
    a.getFullYear() === b.getFullYear() &&
    a.getMonth() === b.getMonth() &&
    a.getDate() === b.getDate()
  );
}

/**
 * The finding at the top of the page, in two parts: the lead (strong) and the detail.
 * Critical first; else high; else calm; else nothing open at all.
 */
export function overviewFinding(
  overview: OverviewView,
  open: readonly ActionSummary[],
  now: Date,
): { lead: Sentence; detail: Sentence | null } {
  const counts = openByPriority(overview);
  const total = ACTION_PRIORITIES.reduce((n, p) => n + counts[p], 0);
  const overdue = num(overview.overdue);
  if (counts.critical > 0) {
    const critical = open.filter((a) => a.levelName === 'critical');
    return {
      lead: { key: 'overview.finding.critical', params: { count: counts.critical } },
      detail: {
        key: 'overview.finding.criticalDetail',
        params: {
          today: critical.filter((a) => a.dueAt && sameDay(new Date(a.dueAt), now)).length,
          unowned: critical.filter((a) => !a.assignedTo).length,
        },
      },
    };
  }
  const next = byDeadline(open)[0];
  const days = next
    ? Math.max(1, Math.round((new Date(next.dueAt!).getTime() - now.getTime()) / DAY))
    : 0;
  if (counts.high > 0)
    return {
      lead: { key: 'overview.finding.noCritical', params: {} },
      detail: { key: 'overview.finding.highDetail', params: { count: counts.high, overdue } },
    };
  if (total > 0)
    return {
      lead: { key: 'overview.finding.calm', params: {} },
      detail: next ? { key: 'overview.finding.calmDetail', params: { days, overdue } } : null,
    };
  return { lead: { key: 'overview.finding.nothing', params: {} }, detail: null };
}

export interface RunwayRow {
  action: ActionSummary;
  priority: UiPriorityLevel;
  /** Percent of the axis. */
  from: number;
  to: number;
  /** Percent of the bar that is time already used. */
  used: number;
  /** The deadline lies beyond the axis; the bar runs to its edge without a tick. */
  clipped: boolean;
}

export interface RunwayAxis {
  days: { at: number; weekend: boolean }[];
  ticks: { at: number; date: Date }[];
  now: number;
}

const RUNWAY_DAYS = 14;

/**
 * The deadline runway (DESIGN.md «Deadline runway»): fourteen days on one axis, today near the
 * middle. A bar runs from the start of an action's clock to its deadline; the solid part is the
 * time used. At most `limit` actions, soonest deadline first.
 */
export function runway(
  rows: readonly ActionSummary[],
  now: Date,
  limit = 8,
): { axis: RunwayAxis; rows: RunwayRow[] } {
  const midnight = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  const start = midnight.getTime() - 6 * DAY;
  const span = RUNWAY_DAYS * DAY;
  const end = start + span;
  const pos = (t: number) => Math.max(0, Math.min(1, (t - start) / span)) * 100;

  const days: RunwayAxis['days'] = [];
  const ticks: RunwayAxis['ticks'] = [];
  for (let i = 0; i < RUNWAY_DAYS; i++) {
    const date = new Date(start + i * DAY);
    days.push({ at: (i / RUNWAY_DAYS) * 100, weekend: date.getDay() === 0 || date.getDay() === 6 });
    if (i % 3 === 0) ticks.push({ at: (i / RUNWAY_DAYS) * 100, date });
  }

  const shown = byDeadline(rows)
    .slice(0, limit)
    .map((action) => {
      const opened = new Date(clockStart(action)).getTime();
      const due = new Date(action.dueAt!).getTime();
      const shownEnd = Math.min(due, end);
      const used =
        shownEnd > opened
          ? Math.max(0, Math.min(1, (now.getTime() - opened) / (shownEnd - opened)))
          : 1;
      return {
        action,
        priority: priorityOf(action.levelName),
        from: pos(opened),
        to: pos(due),
        used: used * 100,
        clipped: due > end,
      };
    });
  return { axis: { days, ticks, now: pos(now.getTime()) }, rows: shown };
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
