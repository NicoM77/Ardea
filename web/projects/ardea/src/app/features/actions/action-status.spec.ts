import { HttpErrorResponse } from '@angular/common/http';
import { problemMessageKey } from '../../core/api/problem';
import { ACTION_PROBLEMS, isStale, storedStatus } from './action-status';

describe('action status', () => {
  it('stores every outcome as a closing and open as open', () => {
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
