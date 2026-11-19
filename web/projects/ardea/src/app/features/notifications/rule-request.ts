import type { NotificationRuleRequest, NotificationRuleView } from '../../core/api/client';
import { num } from '../actions/action-format';

/** The rule as the API takes it back: the same rule, with whatever the caller changes on top. */
export function ruleRequest(
  rule: NotificationRuleView,
  change: Partial<NotificationRuleRequest> = {},
): NotificationRuleRequest {
  return {
    name: rule.name,
    enabled: rule.enabled,
    projectIds: [...rule.projectIds],
    minLevel: num(rule.minLevel),
    events: [...rule.events],
    targets: rule.targets.map((t) => ({
      channel: t.channel,
      recipientKind: t.recipientKind,
      recipientRef: t.recipientRef,
      mode: t.mode,
      digestCron: t.digestCron,
      sendEmpty: t.sendEmpty,
    })),
    ...change,
  };
}
