import { learnedHit } from '../actions/learned-note';
import { share, weeksWithActivity } from './learning-format';

describe('learning format', () => {
  it('tells weeks with any learning from weeks of zeros', () => {
    const week = {
      week: '2026-11-16',
      learned: 0,
      adopted: '0',
      confirmed: 0,
      rejected: 0,
      withdrawn: 0,
    };
    expect(weeksWithActivity([])).toBe(false);
    expect(weeksWithActivity([week, week])).toBe(false);
    expect(weeksWithActivity([week, { ...week, rejected: '2' }])).toBe(true);
  });

  it('shows the share of unknown product keys as a percentage, a dash before the first collection', () => {
    expect(share(0.1234, 'en')).toBe('12.3%');
    expect(share('0.5', 'en')).toBe('50%');
    expect(share(null, 'en')).toBe('–');
  });

  it('reads the learned entry of a hit from its evidence, and nothing from other evidence', () => {
    expect(
      learnedHit({
        match: {},
        learned: { productKey: 'widget-server', rawKeys: ['acme/widgetserver', 7] },
      }),
    ).toEqual({ productKey: 'widget-server', rawKeys: ['acme/widgetserver'] });
    expect(learnedHit({ match: { type: 'product' } })).toBeNull();
    expect(learnedHit({ learned: { productKey: 3 } })).toBeNull();
    expect(learnedHit(null)).toBeNull();
  });
});
