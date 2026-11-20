import type { AuditEventView } from '../../core/api/client';

type Data = Record<string, unknown>;
type Params = Record<string, string>;

interface Spec {
  sentence: string;
  detail?: string;
}

export interface AuditText {
  sentence: { key: string; params: Params };
  detail: { key: string; params: Params } | null;
}

const SPECS: Record<string, Spec> = {
  'org.created': {
    sentence: 'settings.audit.actions.orgCreated',
    detail: 'settings.audit.detail.named',
  },
  'org.settings_changed': { sentence: 'settings.audit.actions.orgSettingsChanged' },
  'org.member_invited': {
    sentence: 'settings.audit.actions.memberInvited',
    detail: 'settings.audit.detail.invited',
  },
  'org.member_joined': {
    sentence: 'settings.audit.actions.memberJoined',
    detail: 'settings.audit.detail.role',
  },
  'org.member_left': { sentence: 'settings.audit.actions.memberLeft' },
  'org.member_removed': { sentence: 'settings.audit.actions.memberRemoved' },
  'org.member_role_changed': {
    sentence: 'settings.audit.actions.memberRoleChanged',
    detail: 'settings.audit.detail.fromTo',
  },
  'org.ownership_transferred': { sentence: 'settings.audit.actions.ownershipTransferred' },
  'org.invitation_revoked': { sentence: 'settings.audit.actions.invitationRevoked' },
  'org.setup_saved': { sentence: 'settings.audit.actions.setupSaved' },
  'org.setup_completed': { sentence: 'settings.audit.actions.setupCompleted' },
  'project.created': {
    sentence: 'settings.audit.actions.projectCreated',
    detail: 'settings.audit.detail.named',
  },
  'project.changed': { sentence: 'settings.audit.actions.projectChanged' },
  'project.archived': { sentence: 'settings.audit.actions.projectArchived' },
  'component.added': {
    sentence: 'settings.audit.actions.componentAdded',
    detail: 'settings.audit.detail.component',
  },
  'component.changed': {
    sentence: 'settings.audit.actions.componentChanged',
    detail: 'settings.audit.detail.component',
  },
  'component.removed': {
    sentence: 'settings.audit.actions.componentRemoved',
    detail: 'settings.audit.detail.component',
  },
  'component.detached': { sentence: 'settings.audit.actions.componentDetached' },
  'profile.replaced': { sentence: 'settings.audit.actions.profileReplaced' },
  'repository_connection.created': {
    sentence: 'settings.audit.actions.connectionCreated',
    detail: 'settings.audit.detail.named',
  },
  'repository_connection.changed': {
    sentence: 'settings.audit.actions.connectionChanged',
    detail: 'settings.audit.detail.named',
  },
  'repository_connection.deleted': { sentence: 'settings.audit.actions.connectionDeleted' },
  'repository_link.created': {
    sentence: 'settings.audit.actions.linkCreated',
    detail: 'settings.audit.detail.repository',
  },
  'repository_link.changed': { sentence: 'settings.audit.actions.linkChanged' },
  'repository_link.deleted': {
    sentence: 'settings.audit.actions.linkDeleted',
    detail: 'settings.audit.detail.repository',
  },
  'repository.indexed': {
    sentence: 'settings.audit.actions.repositoryIndexed',
    detail: 'settings.audit.detail.indexed',
  },
  'repository.shrink_held': { sentence: 'settings.audit.actions.shrinkHeld' },
  'repository.shrink_applied': { sentence: 'settings.audit.actions.shrinkApplied' },
  'repository.shrink_confirmed': { sentence: 'settings.audit.actions.shrinkConfirmed' },
  'repository.unavailable': {
    sentence: 'settings.audit.actions.repositoryUnavailable',
    detail: 'settings.audit.detail.repository',
  },
  'suggestion.accepted': {
    sentence: 'settings.audit.actions.suggestionAccepted',
    detail: 'settings.audit.detail.product',
  },
  'suggestion.dismissed': {
    sentence: 'settings.audit.actions.suggestionDismissed',
    detail: 'settings.audit.detail.product',
  },
  'action.status_changed': {
    sentence: 'settings.audit.actions.actionStatusChanged',
    detail: 'settings.audit.detail.action',
  },
  'finding.status_changed': {
    sentence: 'settings.audit.actions.findingStatusChanged',
    detail: 'settings.audit.detail.fromTo',
  },
  'finding.verdict_reviewed': {
    sentence: 'settings.audit.actions.findingVerdictReviewed',
    detail: 'settings.audit.detail.fromTo',
  },
  'notice.answered': {
    sentence: 'settings.audit.actions.noticeAnswered',
    detail: 'settings.audit.detail.answer',
  },
  'notice.reopened': { sentence: 'settings.audit.actions.noticeReopened' },
  'notify.address_added': {
    sentence: 'settings.audit.actions.addressAdded',
    detail: 'settings.audit.detail.address',
  },
  'notify.address_deleted': {
    sentence: 'settings.audit.actions.addressDeleted',
    detail: 'settings.audit.detail.address',
  },
  'notify.endpoint_created': {
    sentence: 'settings.audit.actions.endpointCreated',
    detail: 'settings.audit.detail.named',
  },
  'notify.endpoint_changed': {
    sentence: 'settings.audit.actions.endpointChanged',
    detail: 'settings.audit.detail.named',
  },
  'notify.endpoint_deleted': {
    sentence: 'settings.audit.actions.endpointDeleted',
    detail: 'settings.audit.detail.named',
  },
  'notify.reminder_rhythm_changed': {
    sentence: 'settings.audit.actions.reminderRhythmChanged',
    detail: 'settings.audit.detail.rhythm',
  },
  'notify.rule_created': {
    sentence: 'settings.audit.actions.ruleCreated',
    detail: 'settings.audit.detail.named',
  },
  'notify.rule_changed': {
    sentence: 'settings.audit.actions.ruleChanged',
    detail: 'settings.audit.detail.named',
  },
  'notify.rule_deleted': {
    sentence: 'settings.audit.actions.ruleDeleted',
    detail: 'settings.audit.detail.named',
  },
};

const NEEDS: Record<string, string[]> = {
  ['settings.audit.detail.named']: ['name'],
  ['settings.audit.detail.invited']: ['email', 'role'],
  ['settings.audit.detail.role']: ['role'],
  ['settings.audit.detail.fromTo']: ['from', 'to'],
  ['settings.audit.detail.answer']: ['answer'],
  ['settings.audit.detail.component']: ['name'],
  ['settings.audit.detail.repository']: ['repository'],
  ['settings.audit.detail.indexed']: ['packages'],
  ['settings.audit.detail.componentName']: ['name'],
  ['settings.audit.detail.product']: ['product'],
  ['settings.audit.detail.address']: ['address'],
  ['settings.audit.detail.rhythm']: ['level5', 'level4', 'level3'],
  ['settings.audit.detail.action']: ['number', 'from', 'to'],
};

export function actorKey(event: AuditEventView): { name: string | null; key: string | null } {
  if (event.actorKind === 'member' && event.actorName) return { name: event.actorName, key: null };
  return {
    name: null,
    key:
      event.actorKind === 'former_member' ? 'settings.audit.formerMember' : 'settings.audit.system',
  };
}

const WORDS = new Set(['from', 'to', 'answer', 'role']);

function scalar(value: unknown): string | null {
  if (value === null || value === undefined || typeof value === 'object') return null;
  const text = String(value).trim();
  return text ? text : null;
}

function params(event: AuditEventView): Params {
  const data: Data =
    event.data && typeof event.data === 'object' && !Array.isArray(event.data)
      ? (event.data as Data)
      : {};
  const out: Params = {};
  for (const [key, value] of Object.entries(data)) {
    const text = scalar(value);
    if (text !== null) out[key] = WORDS.has(key) ? text.replaceAll('_', ' ') : text;
  }
  const owner = scalar(data['owner']);
  const repo = scalar(data['name']);
  if (owner && repo) out['repository'] = `${owner}/${repo}`;
  if (event.action.startsWith('component.')) {
    const component = scalar(data['customName']) ?? scalar(data['productKey']);
    if (component) out['name'] = component;
  }
  if (event.action.startsWith('suggestion.') && out['productKey'])
    out['product'] = out['productKey'];
  if (event.action === 'action.status_changed' && event.subjectId) out['number'] = event.subjectId;
  return out;
}

export function describeAudit(event: AuditEventView): AuditText {
  const spec = SPECS[event.action];
  if (!spec) {
    const object = (event.subjectType ?? '').replaceAll('_', ' ').trim();
    return object
      ? { sentence: { key: 'settings.audit.fallback', params: { object } }, detail: null }
      : { sentence: { key: 'settings.audit.fallbackBare', params: {} }, detail: null };
  }
  const values = params(event);
  const detail =
    spec.detail === 'settings.audit.detail.component' && values['version'] === undefined
      ? 'settings.audit.detail.componentName'
      : spec.detail;
  const needs = detail ? (NEEDS[detail] ?? []) : [];
  const complete = detail && needs.every((name) => values[name] !== undefined);
  return {
    sentence: { key: spec.sentence, params: {} },
    detail: complete && detail ? { key: detail, params: values } : null,
  };
}
