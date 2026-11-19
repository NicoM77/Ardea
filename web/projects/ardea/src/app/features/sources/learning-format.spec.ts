import { learnedHit } from '../actions/learned-note';
import { share } from './learning-format';

describe('learning format', () => {
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
