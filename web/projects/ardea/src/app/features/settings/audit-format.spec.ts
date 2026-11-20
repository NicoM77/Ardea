import type { AuditEventView } from '../../core/api/client';
import { actorKey, describeAudit } from './audit-format';

const event = (extra: Partial<AuditEventView>): AuditEventView => ({
  id: 'e1',
  at: '2026-11-19T08:00:00Z',
  action: 'project.created',
  subjectType: 'project',
  subjectId: 'p1',
  data: null,
  origin: 'ui',
  actorKind: 'member',
  actorName: 'Ada',
  ...extra,
});

describe('audit format', () => {
  it('names members and says who the others were', () => {
    expect(actorKey(event({}))).toEqual({ name: 'Ada', key: null });
    expect(actorKey(event({ actorKind: 'former_member', actorName: null })).key).toBe(
      'settings.audit.formerMember',
    );
    expect(actorKey(event({ actorKind: 'system', actorName: null })).key).toBe(
      'settings.audit.system',
    );
  });

  it('describes known events with only the fields that matter', () => {
    const status = describeAudit(
      event({
        action: 'action.status_changed',
        subjectId: '12',
        data: { from: 'in_progress', to: 'done' },
      }),
    );
    expect(status.sentence.key).toBe('settings.audit.actions.actionStatusChanged');
    expect(status.detail).toEqual({
      key: 'settings.audit.detail.action',
      params: { from: 'in progress', to: 'done', number: '12' },
    });
    const repo = describeAudit(
      event({
        action: 'repository_link.created',
        data: { owner: 'acme', name: 'shop', path: '/' },
      }),
    );
    expect(repo.detail?.params['repository']).toBe('acme/shop');
  });

  it('names a component by product or custom name and drops a missing version', () => {
    const named = describeAudit(
      event({ action: 'component.added', data: { productKey: 'nginx', version: '1.27' } }),
    );
    expect(named.detail?.key).toBe('settings.audit.detail.component');
    expect(named.detail?.params['name']).toBe('nginx');
    const custom = describeAudit(event({ action: 'component.removed', data: { customName: 'X' } }));
    expect(custom.detail?.key).toBe('settings.audit.detail.componentName');
  });

  it('leaves the detail out when its fields are missing', () => {
    expect(describeAudit(event({ action: 'notify.rule_changed', data: null })).detail).toBeNull();
    expect(describeAudit(event({ action: 'notice.reopened', data: { a: 1 } })).detail).toBeNull();
  });

  it('falls back to a generic sentence for unknown event types', () => {
    expect(describeAudit(event({ action: 'x.y', subjectType: 'new_thing' })).sentence).toEqual({
      key: 'settings.audit.fallback',
      params: { object: 'new thing' },
    });
    expect(describeAudit(event({ action: 'x.y', subjectType: null })).sentence.key).toBe(
      'settings.audit.fallbackBare',
    );
  });
});
