import {
  actionCountParams,
  actionFiltersFrom,
  actionFiltersQuery,
  actionListParams,
  isFiltered,
  togglePriority,
} from './list-filters';

const PROJECT = '0190a000-0000-7000-8000-000000000010';

describe('action list filters', () => {
  it('reads only values the list knows from the address, priorities in the list’s order', () => {
    expect(
      actionFiltersFrom({
        view: 'closed',
        priority: 'low,critical,bogus',
        project: PROJECT,
        q: ' pg ',
      }),
    ).toEqual({ view: 'closed', priorities: ['critical', 'low'], project: PROJECT, q: 'pg' });
    expect(actionFiltersFrom({ view: 'all', project: 'not-an-id' })).toEqual({
      view: 'open',
      priorities: [],
      project: '',
      q: '',
    });
  });

  it('writes the address without defaults and reads it back the same', () => {
    const filters = { view: 'open' as const, priorities: ['high' as const], project: '', q: '' };
    const query = actionFiltersQuery(filters);
    expect(query).toEqual({ view: null, priority: 'high', project: null, q: null });
    expect(actionFiltersFrom(query)).toEqual(filters);
  });

  it('asks the API for open and in review together, and for priorities as levels', () => {
    expect(
      actionListParams(
        { view: 'open', priorities: ['critical', 'high'], project: PROJECT, q: 'pg' },
        'c1',
      ),
    ).toEqual({
      status: 'active',
      level: '5,4',
      project: PROJECT,
      q: 'pg',
      cursor: 'c1',
      limit: 50,
    });
    expect(
      actionListParams({ view: 'closed', priorities: [], project: '', q: '' }, undefined),
    ).toEqual({
      status: 'closed',
      limit: 50,
    });
  });

  it('counts for the chips without the priorities, so each chip says what it would leave', () => {
    expect(
      actionCountParams({ view: 'open', priorities: ['low'], project: PROJECT, q: '' }),
    ).toEqual({ status: 'active', project: PROJECT, limit: 100 });
  });

  it('presses and releases a priority chip', () => {
    const none = { view: 'open' as const, priorities: [], project: '', q: '' };
    const high = togglePriority(none, 'high');
    expect(high.priorities).toEqual(['high']);
    expect(togglePriority(togglePriority(high, 'critical'), 'high').priorities).toEqual([
      'critical',
    ]);
    expect(isFiltered(none)).toBe(false);
    expect(isFiltered(high)).toBe(true);
  });
});
