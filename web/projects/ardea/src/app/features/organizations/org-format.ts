import type { MemberView, SessionOrganization } from '../../core/api/client';
import type { Me } from '../../core/session/session';

export type Role = 'owner' | 'admin' | 'member' | 'viewer';

export const ROLE_LABELS: Record<string, string> = {
  owner: 'team.roles.owner',
  admin: 'team.roles.admin',
  member: 'team.roles.member',
  viewer: 'team.roles.viewer',
};

export const ROLE_HINTS: Record<string, string> = {
  owner: 'team.roleHints.owner',
  admin: 'team.roleHints.admin',
  member: 'team.roleHints.member',
  viewer: 'team.roleHints.viewer',
};

export const ASSIGNABLE_ROLES: readonly Role[] = ['admin', 'member', 'viewer'];

const RANK: Record<string, number> = { owner: 3, admin: 2, member: 1, viewer: 0 };

export function roleRank(role: string | null | undefined): number {
  return RANK[role ?? ''] ?? -1;
}

export function canManage(role: string | null | undefined): boolean {
  return roleRank(role) >= RANK['admin'];
}

export interface MemberActions {
  changeRole: boolean;
  remove: boolean;
  leave: boolean;
  transfer: boolean;
}

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

export function organizationBySlug(
  user: Me | null,
  slug: string | null | undefined,
): SessionOrganization | null {
  return user?.organizations.find((org) => org.slug === slug) ?? null;
}

export function mfaBlocked(user: Me | null, organization: SessionOrganization | null): boolean {
  return !!organization?.mfaRequired && user?.twoFactorEnabled !== true;
}

export const SLUG_MAX_LENGTH = 50;
export const SLUG_PATTERN = /^[a-z0-9]([a-z0-9-]{0,48}[a-z0-9])?$/;

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

export function sameEmail(a: string | null | undefined, b: string | null | undefined): boolean {
  return !!a && !!b && a.trim().toLowerCase() === b.trim().toLowerCase();
}
