import type { UiPriorityLevel } from 'ui';
import type { FindingView, MemberView, ModelAnswersView } from '../../core/api/client';
import type { ProblemMessages } from '../../core/api/problem';
import { num, priorityOfLevel } from './action-format';

/**
 * The model's assessment as the pages show it (master plan 7.5, 7.6, 7.9; tasks 4.3, 4.5, 4.7).
 * Its texts arrive as data and are always shown as plain text: Angular interpolation, never
 * `innerHTML`, never a link built from them. Links come from the numbered source list only.
 */

export const VERDICTS = ['affected', 'likely', 'uncertain', 'unlikely', 'not_affected'] as const;
export type Verdict = (typeof VERDICTS)[number];

/** A level the person may expect instead: the five, highest first (master plan 7.6). */
export const EXPECTED_LEVELS = [5, 4, 3, 2, 1] as const;

/** Whether a finding carries the model's verdict that counted, open for a person to review. */
export function hasModelEstimate(f: Pick<FindingView, 'verdictSource' | 'status'>): boolean {
  return f.verdictSource === 'model' && f.status === 'open';
}

/** The model's «not affected» is a proposal a person confirms (master plan 7.5). */
export function isProposal(f: Pick<FindingView, 'verdictSource' | 'verdict'>): boolean {
  return f.verdictSource === 'model' && f.verdict === 'not_affected';
}

/**
 * The level the rules give without the model's verdict, stored in the factors when the verdict
 * counted (ADR 037): the «from» of «High → Medium with the model». Null when it did not count.
 */
export function levelWithoutModel(factors: unknown): UiPriorityLevel | null {
  if (!factors || typeof factors !== 'object') return null;
  const value = (factors as Record<string, unknown>)['levelWithoutModel'];
  return typeof value === 'number' || typeof value === 'string' ? priorityOfLevel(value) : null;
}

/** Whether the floor of the model held the level (rule set bound `model_max_drop`). */
export function heldByFloor(factors: unknown): boolean {
  if (!factors || typeof factors !== 'object') return false;
  const floors = (factors as Record<string, unknown>)['floors'];
  return (
    Array.isArray(floors) &&
    floors.some((b) => (b as Record<string, unknown> | null)?.['bound'] === 'model_max_drop')
  );
}

/** Who reviewed, in the reader's words; a person who left is «someone» (ICU select). */
export function reviewer(f: Pick<FindingView, 'review'>, members: readonly MemberView[]): string {
  const by = f.review?.by;
  return (by && members.find((m) => m.userId === by)?.displayName) || 'someone';
}

/** The numbers of a step's or a sentence's sources, for the small reference marks. */
export function refs(numbers: readonly (number | string)[]): number[] {
  return numbers.map((n) => num(n)).filter((n) => n > 0);
}

/** «4 210 tokens in, 380 out, 3.2 s»: the figures of the budget line, as numbers. */
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

/** Why a review did not go through (task 4.7). */
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

/** Why a request for the model's assessment did not go through (task 4.3). */
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

/** A refusal that means the page showed an old state and must read the finding again. */
export function isStaleReview(code: string | undefined): boolean {
  return code === 'verdict_conflict' || code === 'finding_closed';
}
