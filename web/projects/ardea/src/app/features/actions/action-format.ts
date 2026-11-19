import type { UiPriorityLevel, UiStatusKind } from 'ui';
import type { ActionSummary } from '../../core/api/client';

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
/** A reminder is open or done with an outcome (master plan 17.5). */
export type ActionStatus = 'open' | Outcome;

export function isOutcome(value: string | null | undefined): value is Outcome {
  return (OUTCOMES as readonly string[]).includes(value ?? '');
}

/** System reasons that mean the flaw is gone from the project, rather than never there. */
const GONE = ['remediated', 'fixed_by_update', 'component_removed', 'project_archived'];

/** The working state shown for an action or a finding: a closed one by its outcome. */
export function statusKindOf(status: string, reason: string | null | undefined): UiStatusKind {
  if (status === 'open') return 'open';
  const outcome = status === 'closed' ? (reason ?? 'remediated') : status;
  if (outcome === 'risk_accepted') return 'accepted';
  return GONE.includes(outcome) ? 'remediated' : 'notAffected';
}

/** The status a closed action shows in its status control: its outcome, or remediated. */
export function shownStatus(action: Pick<ActionSummary, 'status' | 'closeReason'>): ActionStatus {
  if (action.status === 'open') return 'open';
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
  moved_to_notice: 'actions.reason.movedToNotice',
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

/** Since when an action stands at its level: the last rise of its level, else when it opened. */
export function clockStart(action: Pick<ActionSummary, 'openedAt' | 'levelRaisedAt'>): string {
  return action.levelRaisedAt ?? action.openedAt;
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

/** The first entry key of an action and how many more it covers («CVE-2026-116325 +3»). */
export function entriesLine(action: Pick<ActionSummary, 'entries' | 'openFindings'>): {
  first: string | null;
  more: number;
} {
  const first = action.entries[0] ?? null;
  return { first, more: Math.max(0, num(action.openFindings) - (first ? 1 : 0)) };
}
