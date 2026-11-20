import type { ActionSummary, OverviewView, SourceStatus } from '../../core/api/client';
import { overviewFinding, projectBlocks, sourceSummary, warningSource } from './overview-format';

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
    openedAt: '2026-11-16T08:36:00Z',
    levelRaisedAt: null,
    closedAt: null,
    closeReason: null,
    isBackfill: false,
    entries: [],
    ...extra,
  };
}

function overview(counts: Record<string, number>): OverviewView {
  return {
    openByLevel: Object.entries(counts).map(([levelName, actions]) => ({
      level: 0,
      levelName,
      actions,
    })),
    urgent: [],
    projects: [
      { project: { id: 'p1', name: 'Platform Ops' }, level: 5, levelName: 'critical', actions: 2 },
    ],
    last24Hours: [],
  };
}

describe('overview format', () => {
  it('leads with the critical actions: how many, how many exploited and how many with a fix', () => {
    const open = [
      action(1, 'critical', { exploitation: 'active' }),
      action(2, 'critical', { fixAvailable: false }),
      action(3, 'high'),
    ];
    expect(overviewFinding(overview({ critical: 2, high: 1 }), open)).toEqual({
      lead: { key: 'overview.finding.critical', params: { count: 2 } },
      detail: { key: 'overview.finding.criticalDetail', params: { exploited: 1, fix: 1 } },
    });
  });

  it('says how many high actions are open when no critical one is', () => {
    const result = overviewFinding(overview({ critical: 0, high: 2 }), [action(3, 'high')]);
    expect(result.lead.key).toBe('overview.finding.noCritical');
    expect(result.detail).toEqual({ key: 'overview.finding.highDetail', params: { count: 2 } });
  });

  it('is calm without critical and high actions, and names no deadline (master plan 17.5)', () => {
    const result = overviewFinding(overview({ critical: 0, high: 0, medium: 1, low: 1 }), [
      action(5, 'medium'),
      action(6, 'low'),
    ]);
    expect(result.lead.key).toBe('overview.finding.calm');
    expect(result.detail).toEqual({ key: 'overview.finding.calmDetail', params: {} });
    expect(overviewFinding(overview({}), []).lead.key).toBe('overview.finding.nothing');
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

  it('sums the sources up in one line and lists only those that need attention', () => {
    const source = (key: string, enabled: boolean, health = 'healthy') =>
      ({ key, name: key, enabled, health }) as SourceStatus;
    expect(sourceSummary([])).toBeNull();
    expect(sourceSummary([source('a', true), source('b', true)])).toEqual({
      key: 'overview.sources.healthy',
      params: { count: 2 },
      attention: [],
    });
    expect(sourceSummary([source('a', false), source('b', false)])).toEqual({
      key: 'overview.sources.paused',
      params: { count: 2 },
      attention: [],
    });
    const failing = source('c', true, 'failing');
    const paused = source('d', false);
    expect(sourceSummary([source('a', true), failing, paused])).toEqual({
      key: 'overview.sources.some',
      params: { count: 3, healthy: 1 },
      attention: [failing, paused],
    });
  });
});
