import type { AuditEventView } from '../../core/api/client';
import { actorKey, dataSummary } from './audit-format';

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

  it('summarises plain values and leaves nested data out', () => {
    expect(dataSummary({ name: 'Shop', criticality: 'high', nested: { a: 1 }, none: null })).toBe(
      'name: Shop, criticality: high',
    );
    expect(dataSummary(null)).toBe('');
    expect(dataSummary(['a'])).toBe('');
  });
});
