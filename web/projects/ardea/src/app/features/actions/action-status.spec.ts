import { HttpErrorResponse } from '@angular/common/http';
import { problemMessageKey } from '../../core/api/problem';
import { ACTION_PROBLEMS, isStale, mainMove, statusOptions, storedStatus } from './action-status';

describe('action status', () => {
  it('offers open and the outcomes on an open action, and only the way back on a closed one', () => {
    expect(statusOptions({ status: 'open', closeReason: null })).toEqual([
      'open',
      'remediated',
      'risk_accepted',
      'not_affected',
    ]);
    expect(statusOptions({ status: 'closed', closeReason: 'risk_accepted' })).toEqual([
      'risk_accepted',
      'open',
    ]);
  });

  it('marks an open action remediated and takes a closing back, nothing in between', () => {
    expect(mainMove('open')).toBe('resolve');
    expect(mainMove('closed')).toBe('reopen');
    expect(storedStatus('not_affected')).toBe('closed');
    expect(storedStatus('open')).toBe('open');
  });

  it('says a 409 status conflict in words and reads the action again', () => {
    const conflict = new HttpErrorResponse({
      status: 409,
      error: { status: 409, code: 'status_conflict', title: 'The action is closed by now' },
    });
    expect(problemMessageKey(conflict, ACTION_PROBLEMS)).toBe('actionDetail.errors.conflict');
    expect(isStale('status_conflict')).toBe(true);
    expect(isStale('action_open')).toBe(true);
    expect(isStale('nothing_to_reopen')).toBe(false);
  });
});
