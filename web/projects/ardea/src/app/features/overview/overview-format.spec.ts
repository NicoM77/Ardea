import type { ActionSummary, OverviewView, SourceStatus } from '../../core/api/client';
import { overviewFinding, projectBlocks, runway, warningSource } from './overview-format';

const NOW = new Date(2026, 10, 18, 8, 36);
const at = (days: number, hours = 0) =>
  new Date(NOW.getTime() + days * 86_400_000 + hours * 3_600_000).toISOString();

function action(
  number: number,
  levelName: string,
  extra: Partial<ActionSummary> = {},
): ActionSummary {
  return {
    number,
    level: { critical: 5, high: 4, medium: 3, low: 2 }[levelName] ?? 1,
    levelName,
    status: 'open',
    project: { id: 'p1', name: 'Platform Ops' },
    component: { id: `c${number}`, name: `C${number}`, version: '1.0', removed: false },
    targetVersion: '1.1',
    openFindings: 1,
    exploitation: 'none',
    fixAvailable: true,
    assignedTo: null,
    dueAt: at(3),
    overdue: false,
    openedAt: at(-2),
    levelRaisedAt: null,
    closedAt: null,
    closeReason: null,
    isBackfill: false,
    entries: [],
    ...extra,
  };
}

function overview(counts: Record<string, number>, overdue = 0): OverviewView {
  return {
    openByLevel: Object.entries(counts).map(([levelName, actions]) => ({
      level: 0,
      levelName,
      actions,
    })),
    overdue,
    urgent: [],
    projects: [
      { project: { id: 'p1', name: 'Platform Ops' }, level: 5, levelName: 'critical', actions: 2 },
    ],
    last24Hours: [],
  };
}

describe('overview format', () => {
  it('leads with the critical actions: how many, due today and without an owner', () => {
    const open = [
      action(1, 'critical', { dueAt: at(0, 2), assignedTo: 'u1' }),
      action(2, 'critical', { dueAt: at(1) }),
      action(3, 'high'),
    ];
    expect(overviewFinding(overview({ critical: 2, high: 1 }), open, NOW)).toEqual({
      lead: { key: 'overview.finding.critical', params: { count: 2 } },
      detail: { key: 'overview.finding.criticalDetail', params: { today: 1, unowned: 1 } },
    });
  });

  it('says how many high actions are open when no critical one is', () => {
    const result = overviewFinding(overview({ critical: 0, high: 2 }, 1), [action(3, 'high')], NOW);
    expect(result.lead.key).toBe('overview.finding.noCritical');
    expect(result.detail).toEqual({
      key: 'overview.finding.highDetail',
      params: { count: 2, overdue: 1 },
    });
  });

  it('is calm without critical and high actions, and says when the next deadline is', () => {
    const result = overviewFinding(
      overview({ critical: 0, high: 0, medium: 1, low: 1 }),
      [action(5, 'medium', { dueAt: at(28) }), action(6, 'low', { dueAt: at(84) })],
      NOW,
    );
    expect(result.lead.key).toBe('overview.finding.calm');
    expect(result.detail).toEqual({
      key: 'overview.finding.calmDetail',
      params: { days: 28, overdue: 0 },
    });
    expect(overviewFinding(overview({}), [], NOW).lead.key).toBe('overview.finding.nothing');
  });

  it('lays the deadlines on fourteen days, soonest first, a far deadline clipped at the edge', () => {
    const { axis, rows } = runway(
      [
        action(1, 'low', { openedAt: at(-1), dueAt: at(60) }),
        action(2, 'critical', { openedAt: at(-1), dueAt: at(1) }),
        action(3, 'high', { dueAt: null }),
      ],
      NOW,
    );
    expect(axis.days).toHaveLength(14);
    expect(axis.now).toBeGreaterThan(42);
    expect(axis.now).toBeLessThan(50);
    expect(rows.map((r) => r.action.number)).toEqual([2, 1]);
    expect(rows[0].used).toBeCloseTo(50, 0);
    expect(rows[0].clipped).toBe(false);
    expect(rows[1].clipped).toBe(true);
    expect(rows[1].to).toBe(100);
  });

  it('gives each project one block per open action, highest priority first', () => {
    const blocks = projectBlocks(overview({}), [
      action(1, 'low'),
      action(2, 'critical'),
      action(3, 'high', { project: { id: 'p2', name: 'Webshop' } }),
    ]);
    expect(blocks).toEqual([
      { id: 'p1', name: 'Platform Ops', count: 2, priorities: ['critical', 'low'] },
    ]);
  });

  it('names a warning’s source from the prefix of its key', () => {
    const sources = [{ key: 'bacs-warnings', name: 'BACS warnings' }] as SourceStatus[];
    expect(warningSource('bacs-warnings:42', sources)).toBe('BACS warnings');
    expect(warningSource('CVE-2026-1', sources)).toBeNull();
  });
});
