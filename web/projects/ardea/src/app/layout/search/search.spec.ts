import { LucideBell } from '@lucide/angular';
import { filterSearchItems, moveActive, opensSearch, SearchItem } from './search';

const item = (label: string, keywords?: string): SearchItem => ({
  label,
  keywords,
  group: 'Example Org',
  url: `/${label}`,
  icon: LucideBell.icon,
});

const items = [
  item('Overview'),
  item('Notification rules'),
  item('Übersicht'),
  item('Two-factor authentication', 'MFA TOTP'),
];

describe('filterSearchItems', () => {
  it('returns everything for an empty query', () => {
    expect(filterSearchItems(items, '  ')).toHaveLength(4);
  });

  it('needs every word, in any order and case', () => {
    expect(filterSearchItems(items, 'RULES notif').map((i) => i.label)).toEqual([
      'Notification rules',
    ]);
  });

  it('ignores accents, so German labels are found without umlauts', () => {
    expect(filterSearchItems(items, 'ubersicht').map((i) => i.label)).toEqual(['Übersicht']);
  });

  it('finds an item by its keywords', () => {
    expect(filterSearchItems(items, 'mfa').map((i) => i.label)).toEqual([
      'Two-factor authentication',
    ]);
  });
});

describe('moveActive', () => {
  it('wraps around at both ends', () => {
    expect(moveActive(2, 1, 3)).toBe(0);
    expect(moveActive(0, -1, 3)).toBe(2);
  });

  it('starts at the first or last option when nothing is active', () => {
    expect(moveActive(-1, 1, 3)).toBe(0);
    expect(moveActive(-1, -1, 3)).toBe(2);
  });

  it('has no active option in an empty list', () => {
    expect(moveActive(0, 1, 0)).toBe(-1);
  });
});

describe('opensSearch', () => {
  const key = (
    k: string,
    mods: Partial<{ ctrlKey: boolean; metaKey: boolean; altKey: boolean }> = {},
  ) => ({
    key: k,
    ctrlKey: false,
    metaKey: false,
    altKey: false,
    ...mods,
  });

  it('opens with / outside a text field only', () => {
    expect(opensSearch(key('/'), false)).toBe(true);
    expect(opensSearch(key('/'), true)).toBe(false);
  });

  it('opens with Ctrl K or Cmd K, even while typing', () => {
    expect(opensSearch(key('k', { ctrlKey: true }), true)).toBe(true);
    expect(opensSearch(key('K', { metaKey: true }), false)).toBe(true);
    expect(opensSearch(key('k'), false)).toBe(false);
  });

  it('leaves Alt combinations to the system and assistive tools', () => {
    expect(opensSearch(key('k', { ctrlKey: true, altKey: true }), false)).toBe(false);
  });
});
