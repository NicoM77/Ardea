import type { ActionSummary } from '../../core/api/client';
import type { ProblemMessages } from '../../core/api/problem';
import { isOutcome, shownStatus, type ActionStatus, type Outcome } from './action-format';

/**
 * What can be done with an action's status (master plan 7.4, 11.5, 17.5; task 3.10). A reminder is
 * open or done: an open one closes with an outcome, a closed one goes back to open by taking the
 * closing back. The API checks the status the person saw (`expectedStatus`) and answers 409
 * `status_conflict` when someone else changed it meanwhile.
 */

export const STATUS_LABELS: Readonly<Record<ActionStatus, string>> = {
  open: 'ui.status.open',
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
    action_open: 'actionDetail.errors.conflict',
    finding_open: 'actionDetail.errors.findingOpen',
    invalid_status_change: 'actionDetail.errors.invalid',
    invalid_note: 'actionDetail.errors.invalid',
  },
  statuses: { 403: 'actionDetail.errors.forbidden', 404: 'actionDetail.errors.gone' },
  fallback: 'actionDetail.errors.failed',
};

/** Whether a refusal means the page showed an old state and must read the action again. */
export function isStale(code: string | undefined): boolean {
  return (
    code === 'status_conflict' ||
    code === 'action_superseded' ||
    code === 'action_closed' ||
    code === 'action_open'
  );
}

/**
 * The statuses the control offers. A closed action keeps its outcome and offers the way back to
 * open; choosing another outcome would need the closing taken back first.
 */
export function statusOptions(
  action: Pick<ActionSummary, 'status' | 'closeReason'>,
): ActionStatus[] {
  if (action.status === 'closed') return [shownStatus(action), 'open'];
  return ['open', 'remediated', 'risk_accepted', 'not_affected'];
}

/** The status the API stores after a change to `to`: outcomes close the action. */
export function storedStatus(to: ActionStatus): 'open' | 'closed' {
  return isOutcome(to) ? 'closed' : to;
}

/** The main move of the moment: mark an open one remediated, or take a closing back. */
export function mainMove(status: string): 'resolve' | 'reopen' {
  return status === 'closed' ? 'reopen' : 'resolve';
}
