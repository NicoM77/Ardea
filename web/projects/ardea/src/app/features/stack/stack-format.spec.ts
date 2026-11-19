import type { StackSuggestionView } from '../../core/api/client';
import { groupCommon, precisionOf, searchHint, suggestionReason } from './stack-format';

const suggestion = (s: Partial<StackSuggestionView>): StackSuggestionView => ({
  productKey: 'x',
  name: 'X',
  kind: 'other',
  reason: 'common',
  basis: null,
  projects: [],
  ...s,
});

describe('stack format', () => {
  it('reads a typed version as the API does (ADR 046): one number or a wildcard is a line', () => {
    expect(precisionOf('')).toBe('none');
    expect(precisionOf('  ')).toBe('none');
    expect(precisionOf('17')).toBe('line');
    expect(precisionOf('8.x')).toBe('line');
    expect(precisionOf('8.3.*')).toBe('line');
    // Many products count releases with two numbers: «8.3» is meant exactly unless someone says otherwise.
    expect(precisionOf('8.3')).toBe('exact');
    expect(precisionOf('17.11')).toBe('exact');
  });

  it('groups the usual entries by the groups of the catalogue search and leaves out the reasoned ones', () => {
    const groups = groupCommon([
      suggestion({ productKey: 'windows-server', kind: 'os', reason: 'companion', basis: 'IIS' }),
      suggestion({ productKey: 'postgresql', kind: 'database' }),
      suggestion({ productKey: 'ubuntu', kind: 'os' }),
      suggestion({ productKey: 'angular', kind: 'framework' }),
      suggestion({ productKey: 'rabbitmq', kind: 'messaging' }),
    ]);
    expect(groups.map((g) => [g.id, g.items.map((i) => i.productKey)])).toEqual([
      ['os', ['ubuntu']],
      ['runtime', ['angular']],
      ['data', ['postgresql', 'rabbitmq']],
    ]);
  });

  it('says why a companion is suggested (ADR 044) and which projects name a product', () => {
    expect(
      suggestionReason(
        suggestion({ productKey: 'windows-server', reason: 'companion', basis: 'IIS' }),
      ),
    ).toEqual({ key: 'stack.suggest.windows', params: { basis: 'IIS' } });
    expect(
      suggestionReason(
        suggestion({ productKey: 'linux-kernel', reason: 'companion', basis: 'Ubuntu' }),
      ).key,
    ).toBe('stack.suggest.kernel');
    expect(
      suggestionReason(suggestion({ reason: 'project', projects: ['Portal', 'Shop'] })).params,
    ).toEqual({ projects: 'Portal, Shop', count: 2 });
  });

  it('explains that Azure is not one product of the catalogue', () => {
    expect(searchHint('Azure')).toBe('stack.add.azureHint');
    expect(searchHint('microsoft azure sql')).toBe('stack.add.azureHint');
    expect(searchHint('nginx')).toBeNull();
  });
});
