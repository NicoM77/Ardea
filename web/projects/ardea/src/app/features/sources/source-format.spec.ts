import type { SourceStatus } from '../../core/api/client';
import { filterOptions, filterSources, healthCounts, interval } from './source-format';

const source = (name: string, extra: Partial<SourceStatus> = {}): SourceStatus => ({
  key: name.toLowerCase(),
  name,
  kind: 'feed',
  operator: 'CVE Program',
  operatorKind: 'mandated',
  enabled: true,
  health: 'healthy',
  healthReason: null,
  intervalMinutes: 60,
  lastSuccessAt: null,
  lastAttemptAt: null,
  nextSyncAt: '2026-11-18T12:00:00Z',
  consecutiveFailures: 0,
  records: 0,
  quarantined: 0,
  attribution: '',
  termsMode: 'full',
  ...extra,
});

const sources = [
  source('OSV', { operator: 'Google', operatorKind: 'recognized' }),
  source('BACS warnings', { operator: 'BACS', operatorKind: 'authority', health: 'degraded' }),
  source('CVE Program'),
  source('CISA KEV', { operator: 'CISA', health: 'failing' }),
];

describe('sources page', () => {
  it('offers each kind of operator once, authorities first, and operators by name', () => {
    expect(filterOptions(sources)).toEqual({
      kinds: ['authority', 'mandated', 'recognized'],
      operators: ['BACS', 'CISA', 'CVE Program', 'Google'],
    });
  });

  it('puts sources that need attention first, then by name', () => {
    expect(filterSources(sources, { operatorKind: '', operator: '' }).map((s) => s.name)).toEqual([
      'CISA KEV',
      'BACS warnings',
      'CVE Program',
      'OSV',
    ]);
  });

  it('filters by kind of operator and by operator', () => {
    expect(
      filterSources(sources, { operatorKind: 'mandated', operator: '' }).map((s) => s.name),
    ).toEqual(['CISA KEV', 'CVE Program']);
    expect(
      filterSources(sources, { operatorKind: 'mandated', operator: 'CISA' }).map((s) => s.name),
    ).toEqual(['CISA KEV']);
  });

  it('counts the health states for the summary', () => {
    expect(healthCounts(sources)).toEqual({ healthy: 2, degraded: 1, failing: 1, disabled: 0 });
  });

  it('prints the interval in the unit people think in', () => {
    expect(interval(30)).toEqual({ key: 'sources.every.minutes', value: 30 });
    expect(interval(360)).toEqual({ key: 'sources.every.hours', value: 6 });
    expect(interval(10080)).toEqual({ key: 'sources.every.days', value: 7 });
  });
});
