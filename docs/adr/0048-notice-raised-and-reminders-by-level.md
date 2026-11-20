# ADR 048: The event `notice.raised`, reminders by level, and escalation dropped

- Status: accepted
- Date: 2026-11-20
- Refines: ADR 038 (notifications), ADR 047 (notices: the `notice.*` history rows are the seam), ADR 029 (system functions)
- Master plan: 8.2, 8.3, 8.4, 8.7, 9.2, 17.5 (E26), task 5a.6

## Context

ADR 047 left the history rows `notice.raised`, `notice.reappeared` and `notice.backfilled` unannounced: nothing told people
about a new notice. Chapter 17.5 replaced escalation (8.7: chains with deadlines) by the reminder: an open reminder reminds by
its level, 5 daily, 4 weekly, 3 monthly, lower ones only in the digests, configurable per organisation (E26, a
recommendation the owner has not confirmed yet). Open were: how a notice, which has no level (17.3), meets the rules' levels;
who hears about a notice; what «once» means for a notice; how a reminder run is scheduled, whom it reaches and in how many
messages; and what is left of escalation.

## Decision

1. **Notices are events of 8.2.** `EventKinds.IsNotifiable` hands `notice.raised`, `notice.reappeared` and
   `notice.backfilled` to the notifications like `action.*` (ADR 038 decision 1, in the event's unit of work). Rules name
   one event, `notice.raised`; it stands for the return of an answered «doesn't affect us» and for the summary of a reverse
   search too (`NotifyEvents.RuleEvent`). Notifications reads notices through `IActionFacts.NoticesAsync` (Assessment's read
   contract): entry, title, score, exploitation, KEV, the components that raised it with project and whether they are in the
   stack, and the reason of a return. Every message links the Notices page `/o/{slug}/notices` (task 5a.5).
2. **A notice meets a rule's threshold by a stand-in level.** It has no level of its own (17.3), but rules filter by level.
   An actively exploited notice or one listed in KEV (`NoticeFacts.Important`) meets every threshold, any other only level
   2 («low and up»); a summary is important when one of its notices is (`important` in the row's data, counted by the
   reverse search). The stand-in is used for the threshold only: the item has no level, so a member's mute applies to it
   (level 5 cannot be muted, a notice can), and push urgency stays normal. A rule's project scope matches the projects of
   the notice's components (the stack is a project), a summary's the project whose reverse search raised it.
3. **Who hears about a notice.** Every member in the app, when an enabled rule of the organisation names `notice.raised`
   and matches it, as for warnings (ADR 038 decision 4); the targets of the matching rules on their channels and modes as
   for any event. The default rules all name `notice.raised` (new organisations through `DefaultRules`, existing ones
   through the migration `RemindersByLevel` for rules with a preset; rules an organisation wrote itself stay as they are).
   With the defaults a plain notice reaches everyone in the app and the Monday summary by e-mail; an important one reaches
   owners and admins at once in the app, by e-mail and by push (rule 1) and everyone's weekday digest (rule 2). This is 8.2's
   «nur In-App und Sammelnachricht; sofort nur bei aktiver Ausnutzung oder KEV».
4. **Once per recipient and channel, its return once more.** `delivery_event.notice_id` (new, indexed) names the notice;
   an immediate message about a notice is decided only when the recipient has no `delivery_event` of the same notice and
   the same kind on that channel. A notice raised anew after a withdrawal, or brought back to «new» because it took over a
   reminder (ADR 047 decision 1), is therefore not announced again to whoever heard of it; `notice.reappeared` (E22) is
   announced once. Digests list what their period noted. A notice answered or withdrawn before its event is evaluated, or
   before its bundled message goes out, is left out of that message. Bundling and the hourly limit are ADR 038's.
5. **The backfill gives one summary.** Quiet notices of a reverse search write no `notice.raised` (ADR 047); their single
   `notice.backfilled` row («14 earlier notices for Stack, 2 of them actively exploited or in KEV») is the only event.
6. **Reminders by level run per organisation on a schedule.** `notify.reminder_schedule` holds per organisation the rhythm
   of levels 5, 4 and 3 (`daily`, `weekly`, `monthly` or `off`; defaults daily, weekly, monthly) and the next run, always
   on weekdays at 08:00 in the organisation's time zone (never at night or at the weekend; a time zone change moves it like
   the digests, ADR 038 decision 14). The instance's minute schedule asks the new system function
   `notify.due_reminder_runs(now)` (organisation ids only; also organisations with rules but no schedule yet, whose first
   run writes the defaults: those created before this version or by the previous build during an upgrade) and queues
   `notify.send-reminders` per organisation. Levels 1 and 2 have no rhythm: the digests name them.
7. **A run.** In one unit of work of the organisation, under the schedule row's lock and checked to be still due: the open
   reminders from level 3 on (`IActionFacts.OpenRemindersAsync`) whose rhythm is complete are due. The rhythm counts from
   the later of the moment the reminder reached its level (opened, or last raised) and its last reminder
   (`notify.reminder_state`), less twelve hours, so a run at 08:00 takes what was raised at 09:00 a day, week or month
   before. A reminder whose level dropped follows its new rhythm from its last reminder; a done one never reminds. Who hears
   about a reminder: the people named by the targets (immediate or digest) of the enabled rules that announce it
   (`action.raised`, its level, its project) on in-app, e-mail and push; webhooks hear about changes, not about what still
   stands. Muting applies per level. Each recipient gets **one message per channel and run** (`delivery.kind = reminder`,
   unique over organisation, recipient, channel and run, `ux_delivery_reminder`), listing every reminder due for them, highest
   level first; each reminder is one event of the run with an id shared by its channels. The messages go out through
   `notify.deliver` with its three steps (master plan 9.2); the run itself talks to nobody outside. When a message is sent,
   the reminders done since are left out and an empty one is suppressed. A failed reminder message does not warn the owners
   (8.6 is about level 5 news). A worker that was down sends one run for the gap.
8. **Settings.** `GET` and `PUT /orgs/{orgId}/reminder-rhythm` (`ReminderRhythmView`, `ReminderRhythmRequest`), owners and
   admins like the rules; the write is an upsert under the row's lock and is audited (`notify.reminder_rhythm_changed`). The
   rules page shows the rhythm per level; the rule editor offers `notice.raised`.
9. **Escalation is dropped (8.7, 17.5, E24).** Nothing in code, settings, API or pages knows escalation, assignment or
   deadline (the acceptance test of ADR 046 decision 12 covers the API). `action.exploitation_escalated` stays: it reports
   that exploitation became known, not an escalation chain. Master plan 8.7 is marked dropped and rewritten as reminders by
   level; the roadmap's «escalation in steps» is gone.
10. **Wording.** Messages call the action a reminder and link `/o/{slug}/reminders/{number}` (task 5a.5; the old `/actions`
    paths redirect). Event names and API identifiers keep «action» (ADR 046 decision 11).
11. **Upgrade (4.11).** The migration adds tables, a nullable column, an index, a function and a wider check; the previous
    build ignores them. Notice events written by the new build and evaluated by an old worker during the upgrade are dropped
    (its `IActionFacts` does not know them); a reminder message an old worker delivers is suppressed as empty. Both last
    only while the two builds overlap.

## Webhook payload (`ardea.notification.v1`, additions)

Each item of `events` may carry, besides `action`, `warning` and `backfill`:

```json
"notice": {
  "id": "…", "state": "open",
  "entry": { "key": "CVE-2026-9010", "title": "PostgreSQL: remote code execution" },
  "cvssScore": 9.8, "exploitation": "active", "kev": true, "reappearedReason": null,
  "components": [ { "name": "PostgreSQL", "version": "17", "project": { "id": "…", "name": "Stack" }, "inStack": true } ]
},
"earlierNotices": { "project": { "id": "…", "name": "Stack" }, "notices": 14, "important": 2 }
```

`event` is `notice.raised`, `notice.reappeared` or `notice.backfilled`; `level` is `null`; `url` is the Notices page.
Reminder runs are not sent to webhooks.

## System functions (ADR 029)

| Function | Flow (master plan 9.2) |
|---|---|
| `notify.due_reminder_runs(now)` | reminders by level: the organisations whose run is due, or with rules but no schedule yet; ids only |

## Alternatives

- **A job per reminder with its own due time (`next_reminder_at`, the first wording of 8.7).** One message per reminder; a
  team with twelve open reminders would get twelve e-mails a morning. One run per organisation bundles them.
- **Calendar rhythms (weekly = every Monday).** A reminder raised on Sunday would remind the next morning, one raised on
  Tuesday after thirteen days. Counting from the level's start keeps the interval.
- **Notices without a threshold (every rule that names them, like warnings).** Then rule 1 would mail every notice to the
  admins at once, against 8.2. A filter of its own per rule would add a field to every rule and the editor.
- **Reminders only to immediate targets.** With the defaults nobody would hear about level 3, and members only in the app.
- **Reminders to webhooks.** A machine hears about every change; a daily repetition is noise for it.
- **State per notice, recipient and channel in a table of its own.** `delivery_event` already holds what was sent to whom;
  a nullable column answers «once» without a second source of truth.

## Consequences

- E26 (the rhythm) stays a recommendation until the owner confirms it; the defaults can change without a migration of the
  rows that were saved (they hold what an admin chose).
- A notice muted on a channel by a member is not delivered there, also an important one.
- 5a.5's Notices page is the target of every notice message; until it is merged the link leads to the not-found page.
