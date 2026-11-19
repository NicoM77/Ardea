import type { ActionEventView, FindingView, MemberView } from '../../core/api/client';
import { historyLines } from './history-format';

const event = (
  kind: string,
  data: object,
  extra: Partial<ActionEventView> = {},
): ActionEventView => ({
  id: `e-${kind}`,
  kind,
  at: '2026-11-17T06:10:00Z',
  actor: null,
  findingId: null,
  data,
  ...extra,
});

const members: MemberView[] = [
  {
    userId: 'u1',
    displayName: 'Lea Brunner',
    email: 'l@example.org',
    role: 'member',
    joinedAt: '',
  },
];
const findings = [
  { id: 'f1', entry: { id: 'x', key: 'CVE-2026-118402', title: null } },
] as FindingView[];

describe('history lines', () => {
  it('lists the newest first, with the people’s names and their own words quoted', () => {
    const lines = historyLines(
      [
        event('action.raised', { level: 4, findings: 3, exploitation: 'none' }),
        event(
          'action.status_changed',
          { from: 'open', to: 'remediated', note: 'Patched' },
          { actor: 'u1' },
        ),
      ],
      members,
      findings,
    );
    expect(lines.map((l) => l.key)).toEqual([
      'actionDetail.history.status',
      'actionDetail.history.raised',
    ]);
    expect(lines[0].params).toMatchObject({ who: 'Lea Brunner', status: 'remediated' });
    expect(lines[0].quote).toBe('Patched');
    expect(lines[1].change).toEqual([null, 'high']);
  });

  it('calls a closing taken back by its name, and a person who left «someone»', () => {
    const [line] = historyLines(
      [
        event(
          'action.status_changed',
          { from: 'closed', to: 'open', note: null },
          { actor: 'gone' },
        ),
      ],
      members,
      findings,
    );
    expect(line.key).toBe('actionDetail.history.reopened');
    expect(line.params['who']).toBe('someone');
  });

  it('names the finding an event is about, and a priority change as two priorities', () => {
    const lines = historyLines(
      [
        event('finding.joined', { level: 2, action: 143 }, { findingId: 'f1' }),
        event('action.level_raised', { from: 3, to: 5 }),
        event(
          'finding.closed',
          { reason: 'fixed_by_update', status: 'remediated' },
          { findingId: 'f1' },
        ),
      ],
      members,
      findings,
    );
    expect(lines[0].reason).toBe('actions.reason.fixedByUpdate');
    expect(lines[1].change).toEqual(['medium', 'critical']);
    expect(lines[2].params).toEqual({ entry: 'CVE-2026-118402' });
  });
});
