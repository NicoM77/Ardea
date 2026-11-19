import type { UiPriorityLevel, UiStatusKind } from 'ui';
import type { ActionSummary, MemberView } from '../../core/api/client';

/**
 * Words and numbers around actions (master plan 7.4, 10.2, 11.4): the API speaks in levels 2 to 5,
 * statuses and reasons; the interface in priorities with their symbol and word, never a numeral.
 */

/** The priorities an action can have, highest first; «Not relevant» never becomes an action. */
export type ActionPriority = 'critical' | 'high' | 'medium' | 'low';
export const ACTION_PRIORITIES: readonly ActionPriority[] = ['critical', 'high', 'medium', 'low'];

/** The level the API filters by, per priority (master plan 7.6). */
export const LEVEL_OF: Readonly<Record<ActionPriority, number>> = {
  critical: 5,
  high: 4,
  medium: 3,
  low: 2,
};

/** The API sends integers that may arrive as strings (OpenAPI `integer | string`). */
export function num(value: number | string | null | undefined): number {
  if (value === null || value === undefined || value === '') return 0;
  return typeof value === 'number' ? value : Number.parseInt(value, 10) || 0;
}

/** The priority of a level name (`critical` … `low`, `not_relevant`). */
export function priorityOf(levelName: string | null | undefined): UiPriorityLevel {
  return levelName === 'critical' ||
    levelName === 'high' ||
    levelName === 'medium' ||
    levelName === 'low'
    ? levelName
    : 'none';
}

/** The priority of a level number, for history lines that carry only the number. */
export function priorityOfLevel(level: number | string | null | undefined): UiPriorityLevel {
  switch (num(level)) {
    case 5:
      return 'critical';
    case 4:
      return 'high';
    case 3:
      return 'medium';
    case 2:
      return 'low';
    default:
      return 'none';
  }
}

/** Outcomes a person can close with; the system has its own reasons (fixed by update, withdrawn …). */
export const OUTCOMES = ['remediated', 'risk_accepted', 'not_affected'] as const;
export type Outcome = (typeof OUTCOMES)[number];
export type ActionStatus = 'open' | 'in_review' | Outcome;

export function isOutcome(value: string | null | undefined): value is Outcome {
  return (OUTCOMES as readonly string[]).includes(value ?? '');
}

/** System reasons that mean the flaw is gone from the project, rather than never there. */
const GONE = ['remediated', 'fixed_by_update', 'component_removed', 'project_archived'];

/** The working state shown for an action or a finding: a closed one by its outcome. */
export function statusKindOf(status: string, reason: string | null | undefined): UiStatusKind {
  if (status === 'open') return 'open';
  if (status === 'in_review') return 'review';
  const outcome = status === 'closed' ? (reason ?? 'remediated') : status;
  if (outcome === 'risk_accepted') return 'accepted';
  return GONE.includes(outcome) ? 'remediated' : 'notAffected';
}

/** The status a closed action shows in its status control: its outcome, or remediated. */
export function shownStatus(action: Pick<ActionSummary, 'status' | 'closeReason'>): ActionStatus {
  if (action.status === 'open' || action.status === 'in_review') return action.status;
  const kind = statusKindOf(action.status, action.closeReason);
  return kind === 'accepted'
    ? 'risk_accepted'
    : kind === 'notAffected'
      ? 'not_affected'
      : 'remediated';
}

/** Why the system closed something, when no person did. */
export const SYSTEM_REASON_LABELS: Readonly<Record<string, string>> = {
  fixed_by_update: 'actions.reason.fixedByUpdate',
  withdrawn: 'actions.reason.withdrawn',
  merged: 'actions.reason.merged',
  component_removed: 'actions.reason.componentRemoved',
  project_archived: 'actions.reason.projectArchived',
  no_longer_matched: 'actions.reason.noLongerMatched',
  out_of_range: 'actions.reason.outOfRange',
  weak_match: 'actions.reason.weakMatch',
};

/** The name of an action: what to do with which component (mockup «Update PostgreSQL to 17.12»). */
export function actionName(
  action: Pick<ActionSummary, 'component' | 'targetVersion' | 'fixAvailable'>,
): { key: string; params: Record<string, string> } {
  const component = action.component.name;
  if (action.targetVersion)
    return { key: 'actions.name.update', params: { component, version: action.targetVersion } };
  if (action.fixAvailable) return { key: 'actions.name.updateAny', params: { component } };
  return { key: 'actions.name.mitigate', params: { component } };
}

export type TimeUnit = 'd' | 'h' | 'm' | 's';
export interface DurationPart {
  value: number;
  unit: TimeUnit;
}

export const UNIT_KEYS: Readonly<Record<TimeUnit, string>> = {
  d: 'time.d',
  h: 'time.h',
  m: 'time.m',
  s: 'time.s',
};

/**
 * A span as the largest units first, at most `parts` of them («1 d 13 h», «5 h 12 m»). Shorter
 * than a minute it counts seconds only when asked to, as the running countdown does.
 */
export function durationParts(ms: number, parts = 2, seconds = false): DurationPart[] {
  let rest = Math.max(0, Math.floor(Math.abs(ms) / 1000));
  const all: DurationPart[] = [
    { value: Math.floor(rest / 86_400), unit: 'd' },
    { value: Math.floor((rest %= 86_400) / 3_600), unit: 'h' },
    { value: Math.floor((rest %= 3_600) / 60), unit: 'm' },
    { value: rest % 60, unit: 's' },
  ];
  const usable = seconds ? all : all.slice(0, 3);
  const first = usable.findIndex((p) => p.value > 0);
  if (first < 0) return [{ value: 0, unit: seconds ? 's' : 'm' }];
  const shown = usable.slice(first, first + parts);
  // «2 h», not «2 h 0 m»; the running countdown keeps its zeros so it does not jump.
  return seconds ? shown : shown.filter((p, i) => i === 0 || p.value > 0);
}

/** Time left to a deadline; negative when it has passed. */
export function timeLeft(dueAt: string, now: Date): number {
  return new Date(dueAt).getTime() - now.getTime();
}

/** How much of an action's time is used up, 0 to 1 (the meter in the list and the runway). */
export function usedShare(startAt: string, dueAt: string, now: Date): number {
  const start = new Date(startAt).getTime();
  const span = new Date(dueAt).getTime() - start;
  if (span <= 0) return 1;
  return Math.max(0, Math.min(1, (now.getTime() - start) / span));
}

/** When an action's clock started: the last rise of its level, else when it opened (master plan 4.9). */
export function clockStart(action: Pick<ActionSummary, 'openedAt' | 'levelRaisedAt'>): string {
  return action.levelRaisedAt ?? action.openedAt;
}

export function median(values: readonly number[]): number | null {
  if (!values.length) return null;
  const sorted = [...values].sort((a, b) => a - b);
  const middle = Math.floor(sorted.length / 2);
  return sorted.length % 2 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
}

/** Per priority: the median time to close and the median time its deadline allowed. */
export interface CloseSpeed {
  priority: ActionPriority;
  count: number;
  took: number | null;
  allowed: number | null;
}

export function closeSpeeds(closed: readonly ActionSummary[]): CloseSpeed[] {
  return ACTION_PRIORITIES.map((priority) => {
    const mine = closed.filter((a) => a.levelName === priority && a.closedAt);
    const took = mine.map((a) => new Date(a.closedAt!).getTime() - new Date(a.openedAt).getTime());
    const allowed = mine
      .filter((a) => a.dueAt)
      .map((a) => new Date(a.dueAt!).getTime() - new Date(clockStart(a)).getTime());
    return { priority, count: mine.length, took: median(took), allowed: median(allowed) };
  });
}

/** How many of the rows each priority has. */
export function countByPriority(rows: readonly ActionSummary[]): Record<ActionPriority, number> {
  const counts: Record<ActionPriority, number> = { critical: 0, high: 0, medium: 0, low: 0 };
  for (const row of rows) {
    const priority = priorityOf(row.levelName);
    if (priority !== 'none') counts[priority]++;
  }
  return counts;
}

/** Who holds the rows: unassigned first, then people by name; each with their rows' priorities. */
export interface OwnerGroup {
  userId: string | null;
  name: string | null;
  priorities: UiPriorityLevel[];
}

export function ownerGroups(
  rows: readonly ActionSummary[],
  members: readonly MemberView[],
): OwnerGroup[] {
  const groups = new Map<string | null, UiPriorityLevel[]>();
  const order = (p: UiPriorityLevel) => ['critical', 'high', 'medium', 'low', 'none'].indexOf(p);
  for (const row of rows) {
    const list = groups.get(row.assignedTo) ?? [];
    list.push(priorityOf(row.levelName));
    groups.set(row.assignedTo, list);
  }
  const named = (id: string | null) =>
    id === null ? null : (members.find((m) => m.userId === id)?.displayName ?? null);
  return [...groups.entries()]
    .map(([userId, priorities]) => ({
      userId,
      name: named(userId),
      priorities: priorities.sort((a, b) => order(a) - order(b)),
    }))
    .sort((a, b) =>
      a.userId === null ? -1 : b.userId === null ? 1 : (a.name ?? '').localeCompare(b.name ?? ''),
    );
}

/** The first entry key of an action and how many more it covers («CVE-2026-116325 +3»). */
export function entriesLine(action: Pick<ActionSummary, 'entries' | 'openFindings'>): {
  first: string | null;
  more: number;
} {
  const first = action.entries[0] ?? null;
  return { first, more: Math.max(0, num(action.openFindings) - (first ? 1 : 0)) };
}
