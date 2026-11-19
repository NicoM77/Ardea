import { groupMetrics } from './operations';

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
