import type { AuditEventView } from '../../core/api/client';

/**
 * Sentences for the audit actions the organization records. An action without one shows its code,
 * so a new action is never hidden, only less readable until it gets its sentence.
 */
export const AUDIT_ACTIONS: Record<string, string> = {
  'org.created': 'settings.audit.actions.orgCreated',
  'org.settings_changed': 'settings.audit.actions.orgSettingsChanged',
  'org.member_invited': 'settings.audit.actions.memberInvited',
  'org.member_joined': 'settings.audit.actions.memberJoined',
  'org.member_left': 'settings.audit.actions.memberLeft',
  'org.member_removed': 'settings.audit.actions.memberRemoved',
  'org.member_role_changed': 'settings.audit.actions.memberRoleChanged',
  'org.ownership_transferred': 'settings.audit.actions.ownershipTransferred',
  'org.invitation_revoked': 'settings.audit.actions.invitationRevoked',
  'org.setup_saved': 'settings.audit.actions.setupSaved',
  'org.setup_completed': 'settings.audit.actions.setupCompleted',
  'project.created': 'settings.audit.actions.projectCreated',
  'project.changed': 'settings.audit.actions.projectChanged',
  'project.archived': 'settings.audit.actions.projectArchived',
  'component.added': 'settings.audit.actions.componentAdded',
  'component.changed': 'settings.audit.actions.componentChanged',
  'component.removed': 'settings.audit.actions.componentRemoved',
  'component.detached': 'settings.audit.actions.componentDetached',
  'profile.replaced': 'settings.audit.actions.profileReplaced',
  'repository_connection.created': 'settings.audit.actions.connectionCreated',
  'repository_connection.changed': 'settings.audit.actions.connectionChanged',
  'repository_connection.deleted': 'settings.audit.actions.connectionDeleted',
};

/** The actor as the log prints it: a member's name, or a translation key for the others. */
export function actorKey(event: AuditEventView): { name: string | null; key: string | null } {
  if (event.actorKind === 'member' && event.actorName) return { name: event.actorName, key: null };
  return {
    name: null,
    key:
      event.actorKind === 'former_member' ? 'settings.audit.formerMember' : 'settings.audit.system',
  };
}

/**
 * A short, readable form of an event's data for the detail line: its plain values, never nested
 * objects. The full data stays in the API for whoever needs it.
 */
export function dataSummary(data: unknown): string {
  if (!data || typeof data !== 'object' || Array.isArray(data)) return '';
  return Object.entries(data as Record<string, unknown>)
    .filter(([, value]) => value !== null && value !== undefined && typeof value !== 'object')
    .map(([key, value]) => `${key}: ${String(value)}`)
    .join(', ');
}
