import type { ActionSummary, UpdateActionRequest } from '../../core/api/client';
import type { ProblemMessages } from '../../core/api/problem';
import { isOutcome, shownStatus, type ActionStatus, type Outcome } from './action-format';

/**
 * What can be done with an action's status (master plan 7.4, 11.5; task 3.10). The API checks the
 * status the person saw (`expectedStatus`) and answers 409 `status_conflict` when someone else
 * changed it meanwhile; a closed action goes back to open by taking the closing back.
 */

export const STATUS_LABELS: Readonly<Record<ActionStatus, string>> = {
  open: 'ui.status.open',
  in_review: 'ui.status.review',
  remediated: 'ui.status.remediated',
  risk_accepted: 'ui.status.accepted',
  not_affected: 'ui.status.notAffected',
};

/** The question a closing asks (mockup: «How was it remediated?»). */
export const CLOSING_QUESTIONS: Readonly<Record<Outcome, string>> = {
  remediated: 'actionDetail.reason.remediated',
  risk_accepted: 'actionDetail.reason.riskAccepted',
  not_affected: 'actionDetail.reason.notAffected',
};

/** Why a change did not go through, in the reader's words. */
export const ACTION_PROBLEMS: ProblemMessages = {
  codes: {
    status_conflict: 'actionDetail.errors.conflict',
    action_superseded: 'actionDetail.errors.superseded',
    nothing_to_reopen: 'actionDetail.errors.nothingToReopen',
    action_closed: 'actionDetail.errors.closed',
    finding_open: 'actionDetail.errors.findingOpen',
    invalid_status_change: 'actionDetail.errors.invalid',
    invalid_action_change: 'actionDetail.errors.invalid',
    invalid_note: 'actionDetail.errors.invalid',
  },
  statuses: { 403: 'actionDetail.errors.forbidden', 404: 'actionDetail.errors.gone' },
  fallback: 'actionDetail.errors.failed',
};

/** Whether a refusal means the page showed an old state and must read the action again. */
export function isStale(code: string | undefined): boolean {
  return code === 'status_conflict' || code === 'action_superseded' || code === 'action_closed';
}

/**
 * The statuses the control offers. A closed action keeps its outcome and offers the way back
 * (open or in review); choosing another outcome would need the closing taken back first.
 */
export function statusOptions(
  action: Pick<ActionSummary, 'status' | 'closeReason'>,
): ActionStatus[] {
  if (action.status === 'closed') return [shownStatus(action), 'open', 'in_review'];
  return ['open', 'in_review', 'remediated', 'risk_accepted', 'not_affected'];
}

/** The status the API stores after a change to `to`: outcomes close the action. */
export function storedStatus(to: ActionStatus): 'open' | 'in_review' | 'closed' {
  return isOutcome(to) ? 'closed' : to;
}

/** The main move of the moment: take it on, give it back, or take a closing back. */
export function mainMove(status: string): 'take' | 'giveBack' | 'reopen' {
  return status === 'open' ? 'take' : status === 'in_review' ? 'giveBack' : 'reopen';
}

/** A PATCH that changes only what is given; the API wants every field, null meaning «unchanged». */
export function actionChange(change: Partial<UpdateActionRequest>): UpdateActionRequest {
  return { assignedTo: null, unassign: null, dueAt: null, resetDueAt: null, ...change };
}

/** Assign to someone, or to no one. */
export function ownerChange(userId: string | null): UpdateActionRequest {
  return userId ? actionChange({ assignedTo: userId }) : actionChange({ unassign: true });
}

/** The local calendar day of a deadline, as a date field shows it (YYYY-MM-DD). */
export function dueDay(dueAt: string | null): string {
  if (!dueAt) return '';
  const d = new Date(dueAt);
  const two = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${two(d.getMonth() + 1)}-${two(d.getDate())}`;
}

/** A new deadline day at the time of day the deadline had, or the end of the day. */
export function dueAtFor(day: string, previous: string | null): string | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(day);
  if (!match) return null;
  const before = previous ? new Date(previous) : null;
  const at = new Date(
    Number(match[1]),
    Number(match[2]) - 1,
    Number(match[3]),
    before ? before.getHours() : 23,
    before ? before.getMinutes() : 59,
  );
  return at.toISOString();
}
