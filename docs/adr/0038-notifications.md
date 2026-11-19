# ADR 038: Notifications: hand-off, evaluation, channels and digests

- Status: accepted
- Date: 2026-11-19
- Refines: ADR 014 (notification state), ADR 023 (one transaction per unit of work), ADR 029 (system functions), ADR 035 (events as history rows)
- Master plan: 4.7, 8, 9.2, 9.5, 10.2, tasks 5.1 to 5.4, 5.6, 5.7

## Context

Chapter 8 defines the events, the rules with their targets, the state per action, recipient and channel, the
bundling of immediate messages, digests, the channels and the acceptance of phase 5 (8.8). ADR 035 made the
history rows of `assess.assess_event` the events. Left open: how an event reaches the notifications without
Assessment knowing them, in which order events are evaluated, what is built in beyond the rules, how a message
stays consistent while it is sent, how external addresses prove themselves without signing in, and how the
digest schedule works.

## Decision

1. **Hand-off in the event's transaction.** `IUnitOfWork.BeforeCommit` runs registered actions inside the
   transaction right before it commits. `History.Add` registers one per unit of work for the events of 8.2 and
   hands their ids to `IActionEventSubscriber` (Assessment.Contracts). Notifications writes them into
   `notify.pending_event` and queues `notify.evaluate` for the organisation (one waiting job per organisation).
   An event and the job that evaluates it commit together or not at all (master plan 3.3). Events are read back
   by id through `IActionFacts`, a read contract of Assessment; Notifications never reads `assess` tables.
2. **One evaluation per organisation at a time, in order.** `notify.evaluate` takes an advisory lock per
   organisation and the pending events ordered by time, 200 at a time. Raise, lower, raise again for the same
   action is therefore evaluated in the order it happened.
3. **The state of 8.4 is written when a message is decided, not when it went out.** Otherwise a second event
   during the two-minute bundling would send the same again. A falling level lowers `last_level` of every
   recipient of the action at once (`ExecuteUpdate`), whether or not they hear the all-clear. Assignments, team
   work, warnings and first fills have no state; `delivery_event` is unique over organisation, recipient,
   channel and event (the organisation is part of it because a warning's event id is its entry, the same in
   every organisation).
4. **Built-in recipients beyond the rules (8.2 «Vorgabe»).** `action.assigned` reaches the assignee in the app
   and by e-mail; `action.status_changed` and `action.note_added` reach the assignee in the app; neither when
   it is their own doing. A warning reaches every member in the app in organisations with an enabled rule for
   warnings that covers all projects. Status changes and notes go to in-app and webhook targets only. An
   all-clear goes to the app always, to other channels only where the action was reported before.
5. **Bundling and the message being sent.** An immediate message on e-mail or webhook waits two minutes
   (`delivery.state = pending`, at most one per recipient and channel, unique partial index). The evaluation
   takes the waiting row `FOR UPDATE`; the delivery job's first step sets it to `sending` under the same lock
   and reads its events after. An event therefore joins a message before it is read or starts a new one; the
   additional state `sending` exists for this. In-app notifications are written at once, one per event.
6. **Delivery in three steps (master plan 9.2).** `notify.deliver` marks and reads in a unit of work of its
   own, calls the channel without a transaction, and writes the outcome in the job's unit of work. A transient
   failure is written as `failed` in a separate unit of work and then thrown, so the job system retries with
   its back-off; 12 attempts reach about 3.5 hours. Permanent failures (recipient refused, 4xx except 408 and
   429, blocked target) end as `dead`. If a level 5 message is given up and the recipient was reached on no
   other channel, the owners get an in-app warning (8.6). Above 60 immediate messages per channel, organisation
   and hour, a new message is `suppressed`; the event stays in the digests that cover it.
7. **Digests.** A rule target in digest mode carries its cron expression (organisation's time zone),
   `next_digest_at` and `last_digest_at`. The instance schedule `notify.find-due-digests` runs every minute and
   asks `notify.due_digest_targets(now)` for due targets, queuing `notify.send-digest` per target in its
   organisation. That job assigns every waiting `digest_item` up to the period end to the period, resolves the
   recipients now, and creates one delivery per recipient, unique over target, recipient and period end. The
   content adds actions that grew in the period without an event (`finding.joined`), marks what the recipient
   already received at once on that channel, and is sent empty when the target says so. A worker that was
   down sends one digest for the gap.
8. **Warnings without identifier.** Notifications subscribes to Intel's `IEntryChangeSubscriber`. A newly
   created, published warning younger than 14 days (older ones arrive with a source's first load) is
   distributed by the system job `notify.distribute-warning`, which asks
   `notify.organizations_with_warning_rules()` and queues `notify.receive-warning` per organisation. Its event
   id is the entry's id.
9. **External addresses.** An admin adds an address; the job `notify.send-address-confirmation` makes a token
   (`c.{organisation}.{address}.{32 random bytes}`), stores its SHA-256 and mails the link; it expires after 7
   days. Every message to an external address carries a signed unsubscribe token (Data Protection, purpose
   `notify.unsubscribe`, no expiry) in the body and as RFC 8058 one-click. Confirm and unsubscribe are anonymous
   `POST`s that the CSRF check lets through because their token, not a cookie, authenticates them
   (`TokenAuthenticated`); the token names the organisation, which becomes the tenant of the unit of work.
10. **Webhooks.** `POST` with JSON (`ardea.notification.v1`) and `Ardea-Signature: t=<unix>,v1=<hex>`, the
    HMAC-SHA256 of `"{t}.{body}"` with the endpoint's secret (`whsec_…`, shown once, rotatable). Address and
    secret are encrypted with purposes `notify.webhook-url` and `notify.webhook-secret`. The address is checked
    when saved and connected through `OutboundAddresses.CreateHandler`: loopback, link-local and metadata never;
    private ranges only for hosts on the instance setting `notify.webhook_internal_hosts`
    (`ARDEA_WEBHOOK_INTERNAL_HOSTS`). No redirects.
11. **Default rules.** `IOrganizationCreatedSubscriber` (Iam.Contracts) creates the three rules of 8.3 in the
    unit of work that creates the organisation; the migration writes them for organisations created before,
    with their digests scheduled on the first run. Push is left out of rule 1 until task 5.5 adds the channel.
    A role target means that role and the ones above it (`admin` = owners and admins, `viewer` = everyone).
12. **Personal settings.** `notify.user_preference` holds muted channels and the language per member and
    organisation; level 5 ignores the mute unless the organisation allows it (`allowMuteCritical` in the
    organisation's settings, next to `emailContent`, full or minimal). Messages are worded in English or German;
    French and Italian fall back to English.

## System functions (ADR 029)

| Function | Flow (master plan 9.2) |
|---|---|
| `notify.organizations_with_warning_rules()` | warning without identifier: the organisations with an enabled rule for warnings over all projects; ids only |
| `notify.due_digest_targets(now)` | digests: the due digest targets, or those without a schedule yet; organisation and target ids only |

## Policies

Organisation tables of `notify` have the tenant policy. `notification`: the member reads and marks their own in
the current organisation; inserts need the current organisation and a member of it as user (the worker writes
for named recipients without a user context). `user_preference`: written by the member only; read by the
member, or by a unit of work of the organisation without a user (the worker). `pending_event` is organisation
data not listed in master plan 4.7.

## Consequences

- Assessment changed in one place (`History`), plus the reader behind `IActionFacts`.
- Web Push (5.5) adds a class for `INotificationChannel`, a subscription table and the push target of rule 1.
- The confirmation and unsubscribe pages and the settings page under `/o/{slug}/settings/notifications` are
  linked from mails but come with task 5.8; until then the API endpoints work, the pages do not exist.
- `repository.unavailable` can be chosen in rules but has no producer until Assets raises it (16.6).
- Rate limit: a suppressed immediate message is not moved into a digest of its own; it appears only in digests
  whose targets match the event anyway.
