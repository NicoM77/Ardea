import type { MemberView, SessionOrganization } from '../../core/api/client';
import type { Me } from '../../core/session/session';
import {
  canManage,
  memberActions,
  mfaBlocked,
  sameEmail,
  SLUG_PATTERN,
  slugify,
} from './org-format';

const member = (userId: string, role: string): MemberView => ({
  userId,
  displayName: userId,
  email: `${userId}@example.org`,
  role,
  joinedAt: '2026-11-18T08:00:00Z',
});

describe('slugify', () => {
  it('derives the address the API derives', () => {
    expect(slugify('Heron Works')).toBe('heron-works');
    expect(slugify('Zürich Énergie AG')).toBe('zurich-energie-ag');
    expect(slugify('  ACME -- Ops & Sec!  ')).toBe('acme-ops-sec');
    expect(slugify('***')).toBe('org');
  });

  it('cuts at 50 characters without a trailing hyphen', () => {
    const slug = slugify(`${'a'.repeat(49)} b`);
    expect(slug).toBe('a'.repeat(49));
    expect(SLUG_PATTERN.test(slugify('x'.repeat(80)))).toBe(true);
  });

  it('accepts only lower-case letters, digits and inner hyphens', () => {
    expect(SLUG_PATTERN.test('heron-works-2')).toBe(true);
    expect(SLUG_PATTERN.test('-heron')).toBe(false);
    expect(SLUG_PATTERN.test('Heron')).toBe(false);
    expect(SLUG_PATTERN.test('a'.repeat(51))).toBe(false);
  });
});

describe('memberActions', () => {
  it('lets administrators change and remove everyone but the owner and themselves', () => {
    expect(memberActions(member('m', 'member'), 'me', 'admin')).toEqual({
      changeRole: true,
      remove: true,
      leave: false,
      transfer: false,
    });
    expect(memberActions(member('o', 'owner'), 'me', 'admin')).toEqual({
      changeRole: false,
      remove: false,
      leave: false,
      transfer: false,
    });
  });

  it('lets the owner hand ownership on, and everyone but the owner leave', () => {
    expect(memberActions(member('a', 'admin'), 'me', 'owner').transfer).toBe(true);
    expect(memberActions(member('me', 'owner'), 'me', 'owner')).toEqual({
      changeRole: false,
      remove: false,
      leave: false,
      transfer: false,
    });
    expect(memberActions(member('me', 'viewer'), 'me', 'viewer').leave).toBe(true);
    expect(memberActions(member('m', 'member'), 'me', 'viewer').remove).toBe(false);
  });

  it('knows who manages the team', () => {
    expect(['owner', 'admin', 'member', 'viewer', undefined].map(canManage)).toEqual([
      true,
      true,
      false,
      false,
      false,
    ]);
  });
});

describe('mfaBlocked', () => {
  const org = (mfaRequired: boolean): SessionOrganization => ({
    id: 'o1',
    slug: 'heron',
    name: 'Heron',
    role: 'member',
    mfaRequired,
  });
  const me = (twoFactorEnabled: boolean): Me => ({
    id: 'u1',
    email: 'a@example.org',
    displayName: 'A',
    isInstanceAdmin: false,
    twoFactorEnabled,
    organizations: [],
  });

  it('holds the pages back only when required and not turned on', () => {
    expect(mfaBlocked(me(false), org(true))).toBe(true);
    expect(mfaBlocked(me(true), org(true))).toBe(false);
    expect(mfaBlocked(me(false), org(false))).toBe(false);
    expect(mfaBlocked(me(false), null)).toBe(false);
  });

  it('compares addresses without case', () => {
    expect(sameEmail(' Jane@Example.org', 'jane@example.org')).toBe(true);
    expect(sameEmail('jane@example.org', null)).toBe(false);
  });
});
