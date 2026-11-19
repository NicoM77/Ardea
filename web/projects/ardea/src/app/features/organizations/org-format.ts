import type { MemberView, SessionOrganization } from '../../core/api/client';
import type { Me } from '../../core/session/session';

/** The roles of a membership, highest first (master plan 9.1). */
export type Role = 'owner' | 'admin' | 'member' | 'viewer';

export const ROLE_LABELS: Record<string, string> = {
  owner: 'team.roles.owner',
  admin: 'team.roles.admin',
  member: 'team.roles.member',
  viewer: 'team.roles.viewer',
};

/** What each role may do, in one sentence, for the invite form and the invitation page. */
export const ROLE_HINTS: Record<string, string> = {
  owner: 'team.roleHints.owner',
  admin: 'team.roleHints.admin',
  member: 'team.roleHints.member',
  viewer: 'team.roleHints.viewer',
};

/** The roles an administrator hands out; ownership only moves by a transfer. */
export const ASSIGNABLE_ROLES: readonly Role[] = ['admin', 'member', 'viewer'];

const RANK: Record<string, number> = { owner: 3, admin: 2, member: 1, viewer: 0 };

export function roleRank(role: string | null | undefined): number {
  return RANK[role ?? ''] ?? -1;
}

/** Owner and administrators invite, change roles, remove members and set the MFA requirement. */
export function canManage(role: string | null | undefined): boolean {
  return roleRank(role) >= RANK['admin'];
}

/** What the signed-in person may do with one row of the member directory. */
export interface MemberActions {
  /** The role is a select instead of a word. */
  changeRole: boolean;
  remove: boolean;
  leave: boolean;
  /** Make this member the owner; only the owner may, and becomes an administrator. */
  transfer: boolean;
}

/**
 * The actions on a member, as the API allows them: administrators change and remove everyone but
 * the owner; each member may leave, the owner only after a transfer; the owner hands ownership on.
 * Nobody changes their own role here: that would only ever lower it, by accident.
 */
export function memberActions(member: MemberView, meId: string, myRole: string): MemberActions {
  const self = member.userId === meId;
  const manage = canManage(myRole) && !self && member.role !== 'owner';
  return {
    changeRole: manage,
    remove: manage,
    leave: self && member.role !== 'owner',
    transfer: myRole === 'owner' && !self,
  };
}

/** The organization in the address, if the signed-in person belongs to it. */
export function organizationBySlug(
  user: Me | null,
  slug: string | null | undefined,
): SessionOrganization | null {
  return user?.organizations.find((org) => org.slug === slug) ?? null;
}

/** The organization asks for two-factor authentication and the person has not turned it on. */
export function mfaBlocked(user: Me | null, organization: SessionOrganization | null): boolean {
  return !!organization?.mfaRequired && user?.twoFactorEnabled !== true;
}

export const SLUG_MAX_LENGTH = 50;
/** The API's rule for an organization's address: lower-case letters, digits and inner hyphens. */
export const SLUG_PATTERN = /^[a-z0-9]([a-z0-9-]{0,48}[a-z0-9])?$/;

/**
 * The address the API derives from a name when none is given (OrganizationDirectory.Slugify):
 * accents dropped, everything else that is not a letter or digit becomes one hyphen.
 */
export function slugify(name: string): string {
  let slug = '';
  for (const c of name.normalize('NFD').toLowerCase()) {
    if (/[a-z0-9]/.test(c)) {
      slug += c;
    } else if (!/\p{Mn}/u.test(c) && slug.length > 0 && !slug.endsWith('-')) {
      slug += '-';
    }
  }
  slug = slug.replace(/^-+|-+$/g, '');
  if (slug.length > SLUG_MAX_LENGTH) slug = slug.slice(0, SLUG_MAX_LENGTH).replace(/-+$/, '');
  return slug || 'org';
}

/** Case-insensitive, as the API compares addresses. */
export function sameEmail(a: string | null | undefined, b: string | null | undefined): boolean {
  return !!a && !!b && a.trim().toLowerCase() === b.trim().toLowerCase();
}
