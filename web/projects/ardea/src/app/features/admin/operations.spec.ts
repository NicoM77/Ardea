import { groupMetrics, notifiedAt } from './operations';

describe('groupMetrics', () => {
  it('groups by group in the operator order, unknown groups last, rows by name', () => {
    const groups = groupMetrics([
      { group: 'zeta', name: 'a', value: 1 },
      { group: 'sources', name: 'osv.lag', value: 12, unit: 'min' },
      { group: 'worker', name: 'alive', value: '2' },
      { group: 'sources', name: 'cve.lag', value: 3, unit: 'min' },
    ]);

    expect(groups.map((g) => g.group)).toEqual(['worker', 'sources', 'zeta']);
    expect(groups[1].rows).toEqual([
      { name: 'cve.lag', value: 3, unit: 'min' },
      { name: 'osv.lag', value: 12, unit: 'min' },
    ]);
    expect(groups[0].rows[0].value).toBe(2);
  });

  it('is empty without figures', () => {
    expect(groupMetrics([])).toEqual([]);
  });
});

describe('notifiedAt', () => {
  const notified = [
    { code: 'source_stale', subject: 'osv', at: '2026-11-19T08:00:00Z' },
    { code: 'worker_stalled', subject: null, at: '2026-11-19T09:00:00Z' },
  ];
  const warning = (code: string, subject?: string) => ({ code, subject, severity: 'warning', message: '' });

  it('matches a warning by code and subject', () => {
    expect(notifiedAt(notified, warning('source_stale', 'osv'))).toBe('2026-11-19T08:00:00Z');
    expect(notifiedAt(notified, warning('worker_stalled'))).toBe('2026-11-19T09:00:00Z');
  });

  it('is null for a warning the administrators were not mailed about', () => {
    expect(notifiedAt(notified, warning('source_stale', 'nvd'))).toBeNull();
    expect(notifiedAt([], warning('worker_stalled'))).toBeNull();
  });
});
