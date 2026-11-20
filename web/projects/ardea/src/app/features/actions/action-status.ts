import type { ProblemMessages } from '../../core/api/problem';
import { isOutcome, type ActionStatus, type Outcome } from './action-format';

export const CLOSING_QUESTIONS: Readonly<Record<Outcome, string>> = {
  remediated: 'actionDetail.reason.remediated',
  risk_accepted: 'actionDetail.reason.riskAccepted',
  not_affected: 'actionDetail.reason.notAffected',
};

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

export function isStale(code: string | undefined): boolean {
  return (
    code === 'status_conflict' ||
    code === 'action_superseded' ||
    code === 'action_closed' ||
    code === 'action_open'
  );
}

export function storedStatus(to: ActionStatus): 'open' | 'closed' {
  return isOutcome(to) ? 'closed' : to;
}
