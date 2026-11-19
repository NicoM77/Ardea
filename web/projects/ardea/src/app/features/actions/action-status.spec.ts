import { HttpErrorResponse } from '@angular/common/http';
import { problemMessageKey } from '../../core/api/problem';
import {
  ACTION_PROBLEMS,
  actionChange,
  dueAtFor,
  dueDay,
  isStale,
  mainMove,
  ownerChange,
  statusOptions,
  storedStatus,
} from './action-status';

describe('action status', () => {
  it('offers every status on an open action, and only the way back on a closed one', () => {
    expect(statusOptions({ status: 'open', closeReason: null })).toEqual([
      'open',
      'in_review',
      'remediated',
      'risk_accepted',
      'not_affected',
    ]);
    expect(statusOptions({ status: 'closed', closeReason: 'risk_accepted' })).toEqual([
      'risk_accepted',
      'open',
      'in_review',
    ]);
  });

  it('takes it on when open, gives it back when in review, takes a closing back when closed', () => {
    expect(mainMove('open')).toBe('take');
    expect(mainMove('in_review')).toBe('giveBack');
    expect(mainMove('closed')).toBe('reopen');
    expect(storedStatus('not_affected')).toBe('closed');
    expect(storedStatus('in_review')).toBe('in_review');
  });

  it('says a 409 status conflict in words and reads the action again', () => {
    const conflict = new HttpErrorResponse({
      status: 409,
      error: { status: 409, code: 'status_conflict', title: 'The action is closed by now' },
    });
    expect(problemMessageKey(conflict, ACTION_PROBLEMS)).toBe('actionDetail.errors.conflict');
    expect(isStale('status_conflict')).toBe(true);
    expect(isStale('nothing_to_reopen')).toBe(false);
  });

  it('sends only what changes: the API reads null as «leave it»', () => {
    expect(ownerChange('u1')).toEqual({
      assignedTo: 'u1',
      unassign: null,
      dueAt: null,
      resetDueAt: null,
    });
    expect(ownerChange(null).unassign).toBe(true);
    expect(actionChange({ resetDueAt: true }).assignedTo).toBeNull();
  });

  it('moves a deadline to another day at the same time of day', () => {
    const previous = new Date(2026, 10, 24, 6, 10).toISOString();
    expect(dueDay(previous)).toBe('2026-11-24');
    const moved = new Date(dueAtFor('2026-11-30', previous)!);
    expect([moved.getDate(), moved.getHours(), moved.getMinutes()]).toEqual([30, 6, 10]);
    expect(new Date(dueAtFor('2026-11-30', null)!).getHours()).toBe(23);
    expect(dueAtFor('', previous)).toBeNull();
  });
});
