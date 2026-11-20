import type { ActionSummary, NoticeComponentRef, NoticeView } from '../../core/api/client';
import {
  affectNote,
  answeredGroups,
  cardMove,
  countText,
  cvssText,
  freshNotices,
  isEarlier,
  latestById,
  noticeOrder,
  openReminderFor,
  reasonPhrase,
  whatParams,
} from './notice-format';

const STACK = { id: 'stack', name: 'Stack' };

function component(extra: Partial<NoticeComponentRef> = {}): NoticeComponentRef {
  return {
    id: 'c-pg',
    name: 'PostgreSQL',
    version: null,
    versionPrecision: 'none',
    noticeThreshold: 'only_important',
    noisy: false,
    project: STACK,
    inStack: true,
    removed: false,
    ...extra,
  };
}

function notice(id: string, extra: Partial<NoticeView> = {}): NoticeView {
  return {
    id,
    state: 'open',
    entry: { id: `e-${id}`, key: `CVE-2026-${id}`, title: `Entry ${id}` },
    cvssScore: 9.1,
    exploitation: 'none',
    raisedAt: '2026-11-18T08:00:00Z',
    isBackfill: false,
    components: [component()],
    decidedBy: null,
    decidedAt: null,
    reminderNumber: null,
    reappearedAt: null,
    reappearedReason: null,
    changedAt: '2026-11-18T08:00:00Z',
    ...extra,
  };
}

describe('notice format', () => {
  it('orders exploited first, then by CVSS score with none last, then newest (E23)', () => {
    const rows = [
      notice('a', { cvssScore: 9.8 }),
      notice('b', { cvssScore: 7.5, exploitation: 'active' }),
      notice('c', { cvssScore: null }),
      notice('d', { cvssScore: '9.8', raisedAt: '2026-11-19T08:00:00Z' }),
    ];
    expect([...rows].sort(noticeOrder).map((n) => n.id)).toEqual(['b', 'd', 'a', 'c']);
  });

  it('writes the score with one decimal and nothing without one', () => {
    expect(cvssText(notice('a', { cvssScore: 10 }))).toBe('10.0');
    expect(cvssText(notice('a', { cvssScore: '9.4' }))).toBe('9.4');
    expect(cvssText(notice('a', { cvssScore: null }))).toBeNull();
  });

  it('keeps notices a backfill raised out of the new ones, under «Earlier»', () => {
    const rows = [
      notice('a'),
      notice('b', { isBackfill: true }),
      notice('c', { state: 'not_affected' }),
    ];
    expect(freshNotices(rows).map((n) => n.id)).toEqual(['a']);
    expect(rows.filter(isEarlier).map((n) => n.id)).toEqual(['b']);
  });

  it('gives the reason from the stack entry, with its line, or from the project that names it', () => {
    expect(reasonPhrase(notice('a'))).toEqual({
      key: 'notices.reason.stack',
      params: { product: 'PostgreSQL' },
    });
    const line = notice('a', {
      components: [component({ version: '17', versionPrecision: 'line' })],
    });
    expect(reasonPhrase(line).params).toEqual({ product: 'PostgreSQL 17' });
    expect(affectNote(line)).toEqual({ key: 'notices.affect.line', params: { line: '17' } });
    expect(affectNote(notice('a'))).toEqual({ key: 'notices.affect.none', params: {} });
    const project = notice('a', {
      components: [
        component({ id: 'gone', removed: true }),
        component({ inStack: false, project: { id: 'p1', name: 'Customer Portal' } }),
      ],
    });
    expect(reasonPhrase(project)).toEqual({
      key: 'notices.reason.project',
      params: { product: 'PostgreSQL', project: 'Customer Portal' },
    });
  });

  it('words «What is it?» from the CVSS band and the exploitation', () => {
    expect(whatParams(notice('a', { cvssScore: 9.0, exploitation: 'active' }))).toEqual({
      severity: 'critical',
      exploitation: 'active',
    });
    expect(whatParams(notice('a', { cvssScore: 6.9 }))).toEqual({
      severity: 'medium',
      exploitation: 'none',
    });
    expect(whatParams(notice('a', { cvssScore: null, exploitation: 'poc' }))).toEqual({
      severity: 'unrated',
      exploitation: 'poc',
    });
  });

  it('finds the open reminder of the same product and place that «Affects us» joins', () => {
    const reminder = (number: number, project: string, name: string, status = 'open') =>
      ({
        number,
        status,
        project: { id: project },
        component: { name },
      }) as unknown as ActionSummary;
    const reminders = [
      reminder(1, 'p1', 'PostgreSQL'),
      reminder(2, 'stack', 'PostgreSQL', 'closed'),
      reminder(3, 'stack', 'postgresql'),
    ];
    expect(openReminderFor(notice('a'), 'stack', reminders)?.number).toBe(3);
    expect(openReminderFor(notice('a'), 'p1', reminders)?.number).toBe(1);
    expect(openReminderFor(notice('a'), 'p2', reminders)).toBeNull();
  });

  it('groups answers by stack entry, answer and reminder, the latest answer first', () => {
    const groups = answeredGroups([
      notice('a', { state: 'not_affected', decidedBy: 'u1', decidedAt: '2026-11-18T09:00:00Z' }),
      notice('b', { state: 'not_affected', decidedBy: 'u2', decidedAt: '2026-11-19T09:00:00Z' }),
      notice('c', {
        state: 'affects_us',
        reminderNumber: '160',
        decidedBy: 'u1',
        decidedAt: '2026-11-17T09:00:00Z',
      }),
      notice('d', { state: 'open' }),
    ]);
    expect(groups.map((g) => [g.answer, g.notices.length, g.reminderNumber, g.decidedBy])).toEqual([
      ['not_affected', 2, null, 'u2'],
      ['affects_us', 1, 160, 'u1'],
    ]);
  });

  it('keeps the newest state of each notice, so a page read before an answer does not undo it', () => {
    const read = notice('a', { changedAt: '2026-11-18T08:00:00Z' });
    const answered = notice('a', { state: 'not_affected', changedAt: '2026-11-19T08:00:00Z' });
    expect(latestById([read], [answered]).map((n) => n.state)).toEqual(['not_affected']);
    const later = notice('a', { state: 'open', changedAt: '2026-11-20T08:00:00Z' });
    expect(latestById([later], [answered]).map((n) => n.state)).toEqual(['open']);
  });

  it('moves with arrows or J and K, and answers with 1 and 2 in any language', () => {
    const key = (k: string, mod: Partial<KeyboardEvent> = {}) =>
      cardMove({ key: k, ctrlKey: false, metaKey: false, altKey: false, ...mod });
    expect(['ArrowDown', 'j', 'ArrowUp', 'K', '1', '2'].map((k) => key(k))).toEqual([
      'next',
      'next',
      'previous',
      'previous',
      'affects',
      'notAffected',
    ]);
    expect(key('a')).toBeNull();
    expect(key('d')).toBeNull();
    expect(key('1', { ctrlKey: true })).toBeNull();
  });

  it('writes the sidebar count short', () => {
    expect(countText(3, false)).toBe('3');
    expect(countText(3, true)).toBe('3+');
    expect(countText(120, false)).toBe('99+');
  });
});
