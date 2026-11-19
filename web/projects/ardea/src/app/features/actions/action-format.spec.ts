import type { ActionSummary } from '../../core/api/client';
import {
  actionName,
  clockStart,
  entriesLine,
  priorityOf,
  priorityOfLevel,
  shownStatus,
  statusKindOf,
  SYSTEM_REASON_LABELS,
} from './action-format';

function sampleAction(extra: Partial<ActionSummary> = {}): ActionSummary {
  return {
    number: 143,
    level: 4,
    levelName: 'high',
    status: 'open',
    project: { id: 'p1', name: 'Platform Ops' },
    component: { id: 'c1', name: 'PostgreSQL', version: '17.9', removed: false },
    targetVersion: '17.12',
    openFindings: 4,
    exploitation: 'none',
    fixAvailable: true,
    openedAt: '2026-11-17T06:10:00Z',
    levelRaisedAt: null,
    closedAt: null,
    closeReason: null,
    isBackfill: false,
    entries: ['CVE-2026-116325', 'CVE-2026-118210'],
    ...extra,
  };
}

describe('action format', () => {
  it('shows a level as its priority word, never a number, and the rest as Not relevant', () => {
    expect(priorityOf('critical')).toBe('critical');
    expect(priorityOf('not_relevant')).toBe('none');
    expect(priorityOf(undefined)).toBe('none');
    expect(priorityOfLevel(5)).toBe('critical');
    expect(priorityOfLevel('2')).toBe('low');
    expect(priorityOfLevel(1)).toBe('none');
  });

  it('shows a closed action by its outcome, and system reasons as gone or not affecting', () => {
    expect(statusKindOf('open', null)).toBe('open');
    expect(statusKindOf('closed', 'risk_accepted')).toBe('accepted');
    expect(statusKindOf('closed', 'fixed_by_update')).toBe('remediated');
    expect(statusKindOf('closed', 'withdrawn')).toBe('notAffected');
    expect(statusKindOf('closed', 'moved_to_notice')).toBe('notAffected');
    expect(statusKindOf('closed', null)).toBe('remediated');
    expect(statusKindOf('not_affected', 'no_longer_matched')).toBe('notAffected');
    expect(shownStatus({ status: 'closed', closeReason: 'not_affected' })).toBe('not_affected');
    expect(shownStatus({ status: 'open', closeReason: null })).toBe('open');
  });

  it('says why the migration to notices closed an action (ADR 046)', () => {
    expect(SYSTEM_REASON_LABELS['moved_to_notice']).toBe('actions.reason.movedToNotice');
  });

  it('names an action by what to do: update to the target, update, or mitigate', () => {
    expect(actionName(sampleAction())).toEqual({
      key: 'actions.name.update',
      params: { component: 'PostgreSQL', version: '17.12' },
    });
    expect(actionName(sampleAction({ targetVersion: null })).key).toBe('actions.name.updateAny');
    expect(actionName(sampleAction({ targetVersion: null, fixAvailable: false })).key).toBe(
      'actions.name.mitigate',
    );
  });

  it('dates an action from the last rise of its level, else from its opening', () => {
    expect(clockStart(sampleAction())).toBe('2026-11-17T06:10:00Z');
    expect(clockStart(sampleAction({ levelRaisedAt: '2026-11-19T08:00:00Z' }))).toBe(
      '2026-11-19T08:00:00Z',
    );
  });

  it('counts the further entries of an action after the first', () => {
    expect(entriesLine(sampleAction())).toEqual({ first: 'CVE-2026-116325', more: 3 });
    expect(entriesLine(sampleAction({ entries: [], openFindings: 0 }))).toEqual({
      first: null,
      more: 0,
    });
  });
});
