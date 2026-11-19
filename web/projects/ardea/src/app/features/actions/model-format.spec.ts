import type { FindingView } from '../../core/api/client';
import { hasModelEstimate, heldByFloor, isProposal, levelWithoutModel } from './model-format';

const finding = (extra: Partial<FindingView>) =>
  ({ verdict: 'unlikely', verdictSource: 'model', status: 'open', ...extra }) as FindingView;

describe('the model’s estimate of a finding', () => {
  it('waits for a person while the model’s verdict counts and the finding is open', () => {
    expect(hasModelEstimate(finding({}))).toBe(true);
    expect(hasModelEstimate(finding({ status: 'in_review' }))).toBe(true);
    expect(hasModelEstimate(finding({ verdictSource: 'human' }))).toBe(false);
    expect(hasModelEstimate(finding({ status: 'not_affected' }))).toBe(false);
  });

  it('marks only the model’s «not affected» as a proposal', () => {
    expect(isProposal(finding({ verdict: 'not_affected' }))).toBe(true);
    expect(isProposal(finding({ verdict: 'not_affected', verdictSource: 'human' }))).toBe(false);
    expect(isProposal(finding({}))).toBe(false);
  });

  it('reads the level without the model and whether its floor held, from the stored factors', () => {
    const factors = {
      level: 3,
      levelWithoutModel: 4,
      floors: [{ bound: 'model_max_drop', level: 3 }],
    };
    expect(levelWithoutModel(factors)).toBe('high');
    expect(heldByFloor(factors)).toBe(true);
    expect(levelWithoutModel({ level: 3 })).toBeNull();
    expect(heldByFloor({ floors: [{ bound: 'active', level: 3 }] })).toBe(false);
    expect(levelWithoutModel(null)).toBeNull();
  });
});
