import { describe, expect, it } from 'vitest';
import type { NotificationRuleView } from '../../core/api/client';
import { matrixRow } from './notify-format';
import { isAppleMobile, keyBytes } from './push-device';

const rule = (
  targets: Partial<NotificationRuleView['targets'][number]>[],
): NotificationRuleView => ({
  id: 'r',
  name: 'High',
  enabled: true,
  projectIds: [],
  minLevel: 4,
  events: ['action.raised'],
  preset: null,
  warnings: [],
  createdAt: '',
  changedAt: '',
  targets: targets.map((t, i) => ({
    id: `t${i}`,
    channel: 'email',
    recipientKind: 'role',
    recipientRef: 'admin',
    recipientLabel: 'admin',
    mode: 'immediate',
    digestCron: null,
    sendEmpty: false,
    nextDigestAt: null,
    ...t,
  })),
});

describe('the matrix of a rule (mockup notification-rules.html)', () => {
  it('puts each target in its channel and names schedules by their preset', () => {
    const row = matrixRow(
      rule([
        { channel: 'in_app', recipientRef: 'viewer' },
        { channel: 'email', mode: 'digest', digestCron: '0 8 * * 1-5', recipientRef: 'viewer' },
        {
          channel: 'email',
          mode: 'digest',
          digestCron: '0 8 * * 1-5',
          recipientKind: 'address',
          recipientLabel: 'a@example.org',
        },
      ]),
    );
    expect(row.push).toBeNull();
    expect(row.webhook).toBeNull();
    expect(row.in_app?.when).toEqual([{ key: 'notify.mode.immediate', cron: null }]);
    // Two targets on one channel with the same schedule are one cell with both recipients.
    expect(row.email?.when).toEqual([{ key: 'notify.schedule.weekdays', cron: '0 8 * * 1-5' }]);
    expect(row.email?.who).toEqual([
      { key: 'notify.recipients.roles.viewer', name: 'viewer' },
      { key: null, name: 'a@example.org' },
    ]);
  });

  it('says when a recipient no longer exists and keeps an unknown schedule as written', () => {
    const row = matrixRow(
      rule([
        {
          channel: 'webhook',
          recipientKind: 'endpoint',
          recipientLabel: null,
          mode: 'digest',
          digestCron: '15 6 * * 2',
        },
      ]),
    );
    expect(row.webhook?.who).toEqual([{ key: 'notify.recipients.gone', name: null }]);
    expect(row.webhook?.when).toEqual([{ key: 'notify.schedule.custom', cron: '15 6 * * 2' }]);
  });
});

describe('push on this device', () => {
  it('turns the base64url key into the bytes PushManager takes', () => {
    const bytes = keyBytes(
      'BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8',
    );
    expect(bytes.length).toBe(65);
    expect(bytes[0]).toBe(4);
  });

  it('knows iPhone and iPad, also an iPad that says it is a Mac', () => {
    expect(isAppleMobile('Mozilla/5.0 (iPhone; CPU iPhone OS 18_1 like Mac OS X)', 5)).toBe(true);
    expect(isAppleMobile('Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)', 5)).toBe(true);
    expect(isAppleMobile('Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)', 0)).toBe(false);
  });
});
