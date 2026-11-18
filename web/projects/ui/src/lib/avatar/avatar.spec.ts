import { initialsOf, tintFor } from './avatar';

describe('initialsOf', () => {
  it('takes the first letter of the first and the last word', () => {
    expect(initialsOf('Ada King Lovelace')).toBe('AL');
  });

  it('uses one letter for a single name and ignores extra spaces', () => {
    expect(initialsOf('  Grace  ')).toBe('G');
  });

  it('keeps letters outside ASCII whole', () => {
    expect(initialsOf('élodie ñúñez')).toBe('ÉÑ');
  });

  it('is empty for an empty name', () => {
    expect(initialsOf('   ')).toBe('');
  });
});

describe('tintFor', () => {
  it('gives the same person the same tint every time', () => {
    expect(tintFor('3f2c9a7e-0001')).toBe(tintFor('3f2c9a7e-0001'));
  });

  it('spreads different people over all three tints', () => {
    const tints = new Set(Array.from({ length: 30 }, (_, i) => tintFor(`person-${i}`)));
    expect(tints).toEqual(new Set(['a', 'b', 'c']));
  });
});
