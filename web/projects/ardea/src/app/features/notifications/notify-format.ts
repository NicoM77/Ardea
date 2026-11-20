import type { UiPriorityLevel } from 'ui';
import type {
  DeliveryView,
  NotificationRuleView,
  NotificationTargetView,
} from '../../core/api/client';
import { num, priorityOfLevel } from '../actions/action-format';

/** The channels of master plan 8.5, in the order the pages show them. */
export type Channel = 'in_app' | 'email' | 'push' | 'webhook';
export const CHANNELS: readonly Channel[] = ['in_app', 'email', 'push', 'webhook'];

export const CHANNEL_LABELS: Readonly<Record<string, string>> = {
  in_app: 'notify.channels.inApp',
  email: 'notify.channels.email',
  push: 'notify.channels.push',
  webhook: 'notify.channels.webhook',
};

/** The channels a member can mute for themselves (webhooks are not people). */
export const PERSONAL_CHANNELS: readonly Channel[] = ['in_app', 'email', 'push'];

/** The events of master plan 8.2 in four groups, each with a word and a hint. */
export interface EventOption {
  value: string;
  label: string;
  hint: string | null;
}

export const EVENT_GROUPS: readonly { label: string; events: readonly EventOption[] }[] = [
  {
    label: 'notify.events.groups.actions',
    events: [
      { value: 'action.raised', label: 'notify.events.raised', hint: 'notify.events.raisedHint' },
      {
        value: 'action.level_raised',
        label: 'notify.events.levelRaised',
        hint: 'notify.events.levelRaisedHint',
      },
      {
        value: 'action.exploitation_escalated',
        label: 'notify.events.exploitation',
        hint: 'notify.events.exploitationHint',
      },
      {
        value: 'action.fix_available',
        label: 'notify.events.fix',
        hint: 'notify.events.fixHint',
      },
      {
        value: 'action.level_lowered',
        label: 'notify.events.levelLowered',
        hint: 'notify.events.allClearHint',
      },
      {
        value: 'action.withdrawn',
        label: 'notify.events.withdrawn',
        hint: 'notify.events.allClearHint',
      },
    ],
  },
  {
    label: 'notify.events.groups.notices',
    events: [
      { value: 'notice.raised', label: 'notify.events.notice', hint: 'notify.events.noticeHint' },
    ],
  },
  {
    label: 'notify.events.groups.team',
    events: [
      {
        value: 'action.status_changed',
        label: 'notify.events.statusChanged',
        hint: 'notify.events.teamHint',
      },
      {
        value: 'action.note_added',
        label: 'notify.events.noteAdded',
        hint: 'notify.events.teamHint',
      },
    ],
  },
  {
    label: 'notify.events.groups.other',
    events: [
      { value: 'warning.published', label: 'notify.events.warning', hint: null },
      {
        value: 'backfill.completed',
        label: 'notify.events.backfill',
        hint: 'notify.events.backfillHint',
      },
      { value: 'repository.unavailable', label: 'notify.events.repository', hint: null },
    ],
  },
];

/** What a new rule starts with: the events of the default rules. */
export const DEFAULT_EVENTS: readonly string[] = [
  'action.raised',
  'action.level_raised',
  'action.exploitation_escalated',
  'action.level_lowered',
  'action.withdrawn',
  'backfill.completed',
  'notice.raised',
];

/** A rule's threshold: level 2 to 5 shown as the priority it reports from. */
export const THRESHOLDS: readonly { level: number; priority: UiPriorityLevel }[] = [
  { level: 2, priority: 'low' },
  { level: 3, priority: 'medium' },
  { level: 4, priority: 'high' },
  { level: 5, priority: 'critical' },
];

export function thresholdPriority(minLevel: number | string): UiPriorityLevel {
  return priorityOfLevel(minLevel);
}

/** The roles a target can name; a role means it and the ones above it. */
export const ROLE_TARGETS: readonly { value: string; label: string }[] = [
  { value: 'admin', label: 'notify.recipients.roles.admin' },
  { value: 'owner', label: 'notify.recipients.roles.owner' },
  { value: 'member', label: 'notify.recipients.roles.member' },
  { value: 'viewer', label: 'notify.recipients.roles.viewer' },
];

export const ROLE_TARGET_LABELS: Readonly<Record<string, string>> = Object.fromEntries(
  ROLE_TARGETS.map((r) => [r.value, r.label]),
);

/** Which channels a kind of recipient can be reached on (the API's rule, master plan 8.3). */
export function channelsFor(kind: string): Channel[] {
  switch (kind) {
    case 'role':
    case 'user':
      return ['in_app', 'email', 'push'];
    case 'address':
      return ['email'];
    case 'endpoint':
      return ['webhook'];
    default:
      return [];
  }
}

/** Digest schedules people pick; a five-field cron in the organization's time zone. */
export const SCHEDULES: readonly { cron: string; label: string }[] = [
  { cron: '0 8 * * 1-5', label: 'notify.schedule.weekdays' },
  { cron: '0 8 * * 1', label: 'notify.schedule.mondays' },
  { cron: '0 8 * * *', label: 'notify.schedule.daily' },
];

export function scheduleLabel(cron: string | null | undefined): string | null {
  return SCHEDULES.find((s) => s.cron === cron?.trim())?.label ?? null;
}

/** One cell of the matrix: how a rule reaches people on one channel. */
export interface MatrixCell {
  /** «At once», a schedule's label, or the cron itself. */
  when: { key: string; cron: string | null }[];
  /** Who: role labels (keys) and names. */
  who: { key: string | null; name: string | null }[];
  sendEmpty: boolean;
}

export function recipientOf(target: NotificationTargetView): {
  key: string | null;
  name: string | null;
} {
  if (target.recipientKind === 'role') {
    return { key: ROLE_TARGET_LABELS[target.recipientRef] ?? null, name: target.recipientRef };
  }
  return {
    key: target.recipientLabel ? null : 'notify.recipients.gone',
    name: target.recipientLabel,
  };
}

/** The matrix of master plan 8.3 as the mockup draws it: one cell per channel, empty where the rule does not reach. */
export function matrixRow(rule: NotificationRuleView): Record<Channel, MatrixCell | null> {
  const row = { in_app: null, email: null, push: null, webhook: null } as Record<
    Channel,
    MatrixCell | null
  >;
  for (const target of rule.targets) {
    const channel = target.channel as Channel;
    if (!CHANNELS.includes(channel)) continue;
    const cell = (row[channel] ??= { when: [], who: [], sendEmpty: false });
    const when =
      target.mode === 'digest'
        ? {
            key: scheduleLabel(target.digestCron) ?? 'notify.schedule.custom',
            cron: target.digestCron,
          }
        : { key: 'notify.mode.immediate', cron: null };
    if (!cell.when.some((w) => w.key === when.key && w.cron === when.cron)) cell.when.push(when);
    const who = recipientOf(target);
    if (!cell.who.some((w) => w.key === who.key && w.name === who.name)) cell.who.push(who);
    cell.sendEmpty ||= target.sendEmpty;
  }
  return row;
}

/** The delivery states with a word and the status symbol that reads closest. */
export const DELIVERY_STATES: readonly string[] = [
  'pending',
  'sending',
  'sent',
  'failed',
  'dead',
  'suppressed',
];

export const DELIVERY_STATE_LABELS: Readonly<Record<string, string>> = {
  pending: 'notify.log.states.pending',
  sending: 'notify.log.states.sending',
  sent: 'notify.log.states.sent',
  failed: 'notify.log.states.failed',
  dead: 'notify.log.states.dead',
  suppressed: 'notify.log.states.suppressed',
};

export function deliveryStatus(state: string): 'open' | 'review' | 'remediated' | 'notAffected' {
  switch (state) {
    case 'sent':
      return 'remediated';
    case 'failed':
    case 'sending':
      return 'review';
    case 'dead':
      return 'notAffected';
    default:
      return 'open';
  }
}

/** How many of the shown rows are still being retried and how many were given up. */
export function logSummary(rows: readonly DeliveryView[]): { failed: number; dead: number } {
  return {
    failed: rows.filter((r) => r.state === 'failed').length,
    dead: rows.filter((r) => r.state === 'dead').length,
  };
}

export const ADDRESS_STATE_LABELS: Readonly<Record<string, string>> = {
  pending: 'notify.addresses.states.pending',
  confirmed: 'notify.addresses.states.confirmed',
  unsubscribed: 'notify.addresses.states.unsubscribed',
  expired: 'notify.addresses.states.expired',
};

/** The level a notification concerns as a priority, or nothing for warnings and team work. */
export function notificationPriority(
  level: number | string | null | undefined,
): UiPriorityLevel | null {
  return level === null || level === undefined ? null : priorityOfLevel(num(level));
}

/** How often open reminders remind, by level (master plan 8.7, 17.5; ADR 048). */
export type ReminderRhythm = 'daily' | 'weekly' | 'monthly' | 'off';
export const REMINDER_RHYTHMS: readonly ReminderRhythm[] = ['daily', 'weekly', 'monthly', 'off'];

export const REMINDER_RHYTHM_LABELS: Readonly<Record<ReminderRhythm, string>> = {
  daily: 'notify.reminders.rhythms.daily',
  weekly: 'notify.reminders.rhythms.weekly',
  monthly: 'notify.reminders.rhythms.monthly',
  off: 'notify.reminders.rhythms.off',
};

export type ReminderLevelField = 'level5' | 'level4' | 'level3';

/** The levels that remind; below level 3 only the digests name a reminder. */
export const REMINDER_LEVELS: readonly { level: 5 | 4 | 3; field: ReminderLevelField }[] = [
  { level: 5, field: 'level5' },
  { level: 4, field: 'level4' },
  { level: 3, field: 'level3' },
];

/** The rhythm each level starts with. */
export const DEFAULT_RHYTHMS: Readonly<Record<ReminderLevelField, ReminderRhythm>> = {
  level5: 'daily',
  level4: 'weekly',
  level3: 'monthly',
};

export function isRhythm(value: string | null | undefined): value is ReminderRhythm {
  return REMINDER_RHYTHMS.includes(value as ReminderRhythm);
}
