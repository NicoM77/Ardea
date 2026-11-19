import type { ActionSummary, MemberView } from '../../core/api/client';
import {
  actionName,
  closeSpeeds,
  durationParts,
  entriesLine,
  ownerGroups,
  priorityOf,
  priorityOfLevel,
  shownStatus,
  statusKindOf,
  usedShare,
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
    assignedTo: null,
    dueAt: '2026-11-24T06:10:00Z',
    overdue: false,
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
    expect(statusKindOf('in_review', null)).toBe('review');
    expect(statusKindOf('closed', 'risk_accepted')).toBe('accepted');
    expect(statusKindOf('closed', 'fixed_by_update')).toBe('remediated');
    expect(statusKindOf('closed', 'withdrawn')).toBe('notAffected');
    expect(statusKindOf('closed', null)).toBe('remediated');
    expect(statusKindOf('not_affected', 'no_longer_matched')).toBe('notAffected');
    expect(shownStatus({ status: 'closed', closeReason: 'not_affected' })).toBe('not_affected');
    expect(shownStatus({ status: 'in_review', closeReason: null })).toBe('in_review');
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

  it('writes a span with its two largest units, and seconds only for the countdown', () => {
    const h = 3_600_000;
    expect(durationParts(37 * h + 28 * 60_000)).toEqual([
      { value: 1, unit: 'd' },
      { value: 13, unit: 'h' },
    ]);
    expect(durationParts(5 * h + 12 * 60_000 + 9_000, 3, true)).toEqual([
      { value: 5, unit: 'h' },
      { value: 12, unit: 'm' },
      { value: 9, unit: 's' },
    ]);
    // Overdue spans count the same way; the sign is said in words.
    expect(durationParts(-2 * h)).toEqual([{ value: 2, unit: 'h' }]);
    expect(durationParts(20_000)).toEqual([{ value: 0, unit: 'm' }]);
  });

  it('measures the share of the time used between the start of the clock and the deadline', () => {
    const now = new Date('2026-11-20T06:10:00Z');
    expect(usedShare('2026-11-17T06:10:00Z', '2026-11-23T06:10:00Z', now)).toBeCloseTo(0.5);
    expect(usedShare('2026-11-17T06:10:00Z', '2026-11-18T06:10:00Z', now)).toBe(1);
    expect(usedShare('2026-11-21T00:00:00Z', '2026-11-23T00:00:00Z', now)).toBe(0);
  });

  it('counts the further entries of an action after the first', () => {
    expect(entriesLine(sampleAction())).toEqual({ first: 'CVE-2026-116325', more: 3 });
    expect(entriesLine(sampleAction({ entries: [], openFindings: 0 }))).toEqual({
      first: null,
      more: 0,
    });
  });

  it('takes the median time to close per priority against the median time allowed', () => {
    const day = 86_400_000;
    const closed = [1, 3, 5].map((days, i) =>
      sampleAction({
        number: i,
        status: 'closed',
        closedAt: new Date(Date.parse('2026-11-01T00:00:00Z') + days * day).toISOString(),
        openedAt: '2026-11-01T00:00:00Z',
        dueAt: '2026-11-08T00:00:00Z',
      }),
    );
    const high = closeSpeeds(closed).find((s) => s.priority === 'high')!;
    expect(high).toEqual({ priority: 'high', count: 3, took: 3 * day, allowed: 7 * day });
    expect(closeSpeeds(closed).find((s) => s.priority === 'critical')!.took).toBeNull();
  });

  it('groups owners with the unassigned first and each person’s priorities highest first', () => {
    const members: MemberView[] = [
      {
        userId: 'u2',
        displayName: 'Nina Meier',
        email: 'n@example.org',
        role: 'owner',
        joinedAt: '',
      },
      {
        userId: 'u1',
        displayName: 'Lea Brunner',
        email: 'l@example.org',
        role: 'member',
        joinedAt: '',
      },
    ];
    const groups = ownerGroups(
      [
        sampleAction({ number: 1, assignedTo: 'u2', levelName: 'low' }),
        sampleAction({ number: 2, assignedTo: 'u2', levelName: 'critical' }),
        sampleAction({ number: 3, assignedTo: 'u1' }),
        sampleAction({ number: 4 }),
      ],
      members,
    );
    expect(groups.map((g) => g.name)).toEqual([null, 'Lea Brunner', 'Nina Meier']);
    expect(groups[2].priorities).toEqual(['critical', 'low']);
  });
});
