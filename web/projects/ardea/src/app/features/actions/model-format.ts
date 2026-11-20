import type { UiPriorityLevel } from 'ui';
import type { FindingView, MemberView, ModelAnswersView } from '../../core/api/client';
import type { ProblemMessages } from '../../core/api/problem';
import { num, priorityOfLevel } from './action-format';

export const VERDICTS = ['affected', 'likely', 'uncertain', 'unlikely', 'not_affected'] as const;

export const EXPECTED_LEVELS = [5, 4, 3, 2, 1] as const;

export function hasModelEstimate(f: Pick<FindingView, 'verdictSource' | 'status'>): boolean {
  return f.verdictSource === 'model' && f.status === 'open';
}

export function isProposal(f: Pick<FindingView, 'verdictSource' | 'verdict'>): boolean {
  return f.verdictSource === 'model' && f.verdict === 'not_affected';
}

export function levelWithoutModel(factors: unknown): UiPriorityLevel | null {
  if (!factors || typeof factors !== 'object') return null;
  const value = (factors as Record<string, unknown>)['levelWithoutModel'];
  return typeof value === 'number' || typeof value === 'string' ? priorityOfLevel(value) : null;
}

export function heldByFloor(factors: unknown): boolean {
  if (!factors || typeof factors !== 'object') return false;
  const floors = (factors as Record<string, unknown>)['floors'];
  return (
    Array.isArray(floors) &&
    floors.some((b) => (b as Record<string, unknown> | null)?.['bound'] === 'model_max_drop')
  );
}

export function reviewer(f: Pick<FindingView, 'review'>, members: readonly MemberView[]): string {
  const by = f.review?.by;
  return (by && members.find((m) => m.userId === by)?.displayName) || 'someone';
}

export function refs(numbers: readonly (number | string)[]): number[] {
  return numbers.map((n) => num(n)).filter((n) => n > 0);
}

export function budget(model: ModelAnswersView): {
  tokensIn: number;
  tokensOut: number;
  seconds: number;
} {
  return {
    tokensIn: num(model.tokensIn),
    tokensOut: num(model.tokensOut),
    seconds: Math.round(num(model.durationMs) / 100) / 10,
  };
}

export const REVIEW_PROBLEMS: ProblemMessages = {
  codes: {
    verdict_conflict: 'estimate.errors.conflict',
    finding_closed: 'estimate.errors.closed',
    nothing_to_confirm: 'estimate.errors.nothingToConfirm',
    invalid_review: 'estimate.errors.invalid',
    finding_not_found: 'estimate.errors.gone',
  },
  statuses: { 403: 'actionDetail.errors.forbidden', 404: 'estimate.errors.gone' },
  fallback: 'actionDetail.errors.failed',
};

export const REQUEST_PROBLEMS: ProblemMessages = {
  codes: {
    ai_not_available: 'aiAssessment.request.notAvailable',
    assessment_requests_exhausted: 'aiAssessment.request.exhausted',
    alert_not_found: 'alert.gone',
  },
  statuses: {
    403: 'aiAssessment.request.forbidden',
    409: 'aiAssessment.request.notAvailable',
    429: 'aiAssessment.request.exhausted',
  },
  fallback: 'actionDetail.errors.failed',
};

export function isStaleReview(code: string | undefined): boolean {
  return code === 'verdict_conflict' || code === 'finding_closed';
}
