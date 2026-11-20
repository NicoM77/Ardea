# ADR 047: Notices from coarse components, their answers and their return

- Status: accepted
- Date: 2026-11-20
- Refines: ADR 035 (history rows and the first fill), ADR 044 (notices per week and thresholds), ADR 046 (the stack,
  version precision and `assess.notice`); master plan 7.4, 7.7, 17.1 to 17.3
- Master plan: 4.11, 7.9, 14 (phase 5a acceptance), 17, task 5a.3

## Context

ADR 046 laid the data model of chapter 17: the stack as a system project, `component.version_precision` and
`notice_threshold`, `assess.notice`, reminders open or done. Until now nothing wrote notices: a hit on a component without
an exact version still went the precise path, and a stop-gap guard in `AlertAssessor` only kept the findings E25 had moved
to a notice from opening an action again (ADR 046 decision 9). ADR 044 set the thresholds and the acceptance (at most 25
notices a week for a stack of ten entries). Task 5a.3 generates notices with these rules, answers them («affects us»
opens a normal reminder, E21; «doesn't affect us» comes back once on exploitation, E22) and replaces the guard. The page is
task 5a.5, the event `notice.raised` and reminders by level task 5a.6. Open were: what becomes of the finding of a coarse
hit, when a notice goes away again, how a stack entry added later avoids a flood, where «affects us» puts the reminder and
what a version given with it does, and whether undo is a time window or a reopen.

## Decision

1. **The component decides the path (17.1).** A strong hit (match type `product`, `cpe` or `package`) on a component whose
   precision is `none` or `line` is a *coarse hit*. Its finding is still written, with match and version statements as
   evidence, but closed by the system as `not_affected` with the reason `moved_to_notice` at level 1; its factors carry the
   exclusion `moved_to_notice` and `preciseLevel`, the level the rules give the hit on the precise path. The version check
   of a coarse hit runs without the version: a line is not a release (the line narrows the notice instead, decision 3).
   Weak hits and components with an exact version are unchanged. The stop-gap guard of ADR 046 decision 9 is gone: a
   finding of the system on a coarse component always returns to the notice, also when the entry becomes exploited; the
   notice reappears instead (decision 6). When the component gets an exact version, the next assessment takes the precise
   path and the rules open the finding; when it loses it, the finding goes to the notice and its reminder is withdrawn
   (`action.withdrawn`, reason `moved_to_notice`). **Nothing is handed over silently (E25):** a finding the system closes
   as it moves to the coarse path while it is open (a reminder) or carries a person's verdict always raises the notice,
   whatever the threshold and the line say. This covers the open actions on components with a line from before this
   change (E25 had left them alone), which move on their next assessment, and repository components whose image tag
   becomes a line.
2. **People's findings on the coarse path.** A finding a person opened by answering «affects us» stays open, at least at
   level 2 with the rules' level and factors otherwise, until a person closes it or the entry stops matching. A finding a
   person closed stays closed: the rules' «level raised» or «in range again» reopen only precise findings, since a coarse
   component has no version to judge; exploitation reaches people through the notice.
3. **When a notice is raised (17.2, 17.3; ADR 044).** A coarse hit counts when the line, if the component has one, leaves
   the entry in: the narrowing rule of ADR 044 decision 5 moved from the harness to `Matching/LineNarrowing` and works on
   the version statements of the check. It raises a notice when the entry meets the component's threshold:
   `everything`, or `only_important`, which is a CVSS base score of 9.0 or more, active exploitation (KEV, SSVC «active»),
   an authority's warning or a high EPSS class; for a product the catalogue marks as noisy only exploitation or a warning.
   The marking is a new curated field `noisy` (catalogue schema, `catalog.product.noisy`, default `false`), set for Google
   Chrome, Microsoft Edge and Mozilla Firefox. No language model takes part.
4. **One notice per organisation and entry, and how long it lasts.** The notice names the coarse components that raised
   it (`component_ids`, the reason «Your stack names PostgreSQL»). Each assessment updates that list within what it
   looked at, so the forward search, a project's reverse search and an answer keep the union. Once raised, an open
   notice stays while one of its components is still hit within its line, whatever later changes do to the score; when
   none is left (exact version, component removed, project archived, entry withdrawn or no longer naming the product), it
   is `withdrawn` (new state; the last reason is kept), and it is raised anew, with a new `raised_at` and the components
   that raise it then (not the ones named before), when a threshold is met again. Answered notices keep their answer; their list follows the hits while any is left. The notice keeps the
   entry's score and exploitation, updated with every assessment, for the order of the list.
5. **Forward and backward, without a flood (ADR 035).** The forward search raises a notice with a history row
   `notice.raised` each. A project's reverse search (its first fill, a stack entry added, a threshold or a line changed)
   uses the look-back of the actions, 180 days, but raises quietly: the notices are marked `is_backfill` and the run writes
   one `notice.backfilled` row with their count for the project, as the first fill of the actions writes one
   `backfill.completed`. The forward search leaves unseen projects to the reverse search as before. **Backfilled notices
   are open but not new:** the list shows them under `state=earlier`, apart from the new ones (`state=new`), so the
   overview's «n new notices» (17.7) counts what arrived since; the page can offer them as «Earlier». Limiting the backfill
   to exploited or KEV entries was rejected: 17.3 raises a notice for every entry that meets the threshold, and a stack
   entry added on Monday should not know less than one added a week before. See the measurement below.
6. **«Doesn't affect us» comes back once (E22; 17.3 «aktiv ausgenutzt oder in KEV aufgenommen»).** The answer stores the
   entry's exploitation and whether CISA's KEV catalogue listed it at that moment (`decided_exploitation`,
   `decided_kev`; Intel's `EntryFacts.InKev`, from the KEV feed or a CVE record's CISA-ADP KEV entry). The notice is raised
   again when, after the answer, the entry was added to KEV (`reappeared_reason` `kev`) or became actively exploited
   otherwise (`exploited`), while a component still names it: state `open`, `reappeared_at`, history row
   `notice.reappeared`. So an answer given while SSVC already said «active» comes back once on a later KEV listing. The
   earlier answer stays on the notice for the card («answered … on …»). `reappeared_at` is set once: a notice returns at
   most once, and answered again it never returns, whatever signal follows.
7. **«Affects us» makes a normal reminder (17.3, E21).** The answer may name a project and a version.
   - Only components the notice names that are still coarse count; one that got an exact version since is on the precise
     path by itself. Without a project, the place is the stack entry the notice names (or every component it names when
     none is in the stack). With a project, the place is that project's component of the product. A version without a
     project for a notice that names components in several projects is refused (`400`, field `projectId`): a version
     belongs to one place.
   - Without a version, a person opens the entry's finding on each coarse place (verdict «affected», source `human`); it
     opens the component's reminder or joins the open one, so a second notice for the same product and place adds to the
     same reminder («Add to reminder»).
   - With a version, it is recorded in the profile through a new Assets contract, `IProfilePlacement`: a component of the
     product with that version is used; else the place's hand-entered component of the product without an exact version
     takes it; else a component is added. The component is then exact and the entry is checked precisely: in range opens
     or joins its reminder, out of range leaves the answer without a reminder. The change is audited and queues the
     project's reverse search like an edit; a stack entry that learns a version leaves the coarse path (17.1). When a
     repository reads the product in that place and no hand-entered component can take the version, the answer is
     refused (`400`, field `version`): the repository's component takes its version from the repository (16.5), and a
     hand-entered one beside it would bring a notice and a reminder for every later entry. Answering without a version, or
     detaching the component, remains. Placements for the same project and product take turns (an advisory lock), so two
     answers at once add one component.
   - `IProfilePlacement` is the first contract through which one module writes another's data. Master plan 3.2 rule 1
     names query interfaces, DTOs and integration events; this is a deliberate exception: the person states a fact about
     the profile on the Notices page, the answer and the profile change must commit together, and an event would leave the
     answer's precise check without the version. Assets keeps every rule of the change (checks, keys, audit, reverse search).
   - The notice links the reminder (`remediation_id`); `action.raised` and `finding.joined` are written as for any reminder.
8. **Undo is a plain reopen.** `POST …/reopen` takes either answer back at any time, with the answer the person saw. Findings
   the «affects us» answer opened and nobody changed since (same person and moment) return to the notice, which withdraws
   their reminder when nothing else keeps it open; a version recorded stays, as it is a statement about the profile. The
   components the notice names are assessed again, so a notice whose components became exact through the answer is
   withdrawn instead of asking again. A window would add a clock to the rule and lose the «Change answer» of the answered
   list (mockup `notices.html`).
9. **API.** `GET /orgs/{orgId}/notices?state=new|earlier|answered|withdrawn&component=` (a page of `NoticeView`, actively
   exploited first, then by score, then newest; cursor paging; the order comes from the computed columns `exploited` and
   `score_rank` and the index `ix_notice_list`, so a page is read in index order), `GET …/notices/{noticeId}`, `POST …/{noticeId}/answer`
   (`AnswerNoticeRequest`: `answer`, `expectedState`, `projectId`, `version`) and `POST …/{noticeId}/reopen`
   (`ReopenNoticeRequest`). Members answer; everyone reads. The answer takes the alert's lock, which every assessment of
   the entry takes, and compares the state the caller saw under it (`409 status_conflict`); one unit of work writes the
   answer, the findings, the reminder, the history row (`notice.answered`, `notice.reopened`, with `notice_id`) and the
   audit entry. The view is read before the commit.
10. **Seam for task 5a.6.** `notice.raised`, `notice.backfilled`, `notice.reappeared`, `notice.answered`,
    `notice.reopened` and `notice.withdrawn` are history rows with the new column `assess_event.notice_id`; they wake the
    live stream like every history row but are not handed to the notifications yet (`EventKinds.IsNotifiable` is
    unchanged).
11. **Upgrade (4.11).** The migrations only add columns with defaults and widen checks (`withdrawn`); the previous build
    neither writes notices nor reads the new columns. `NoticeGeneration` copies score and exploitation to the notices of
    E25 from `intel.security_entry`: a third cross-schema step after the two of ADR 046 decision 10, skipped where Intel's
    schema is missing; Intel migrates before Assess. Its `Down` makes withdrawn notices open again before it restores the
    earlier checks.
12. **The reference set keeps measuring the rules.** Its evaluation reads `preciseLevel` for coarse hits, so the 103 pairs
    without a version still calibrate the rules (master plan 7.9); all thresholds hold.

## Measurement

The opt-in `NoticeSpike` now runs the real generation: one organisation per stack, its entries in the stack project with
the default threshold (and the line where the stack names one), every record of the spike week through the pipeline,
the notices read from `assess.notice`.

| Stack (ten entries unless noted) | Notices in `assess.notice` | of them new in the week | ADR 044's count |
|---|--:|--:|--:|
| Web agency: .NET, Angular, Azure | **14** | 0 | 14 |
| Hoster: PHP, WordPress, TYPO3 on Linux | **4** | 1 | 4 |
| Enterprise IT: Windows, Exchange, FortiGate, VMware | **19** | 3 | 19 |
| SaaS: Java, React, Kubernetes | **3** | 0 | 3 |
| Spike A (4 entries) | 1 | 0 | 1 |
| Spike B (3 entries) | 3 | 1 | 3 |
| Spike C (4 entries) | 1 | 0 | 1 |

The generation gives exactly the notices the harness counted, per stack entry as well (Chrome: one, the exploited
entry; Windows Server: the twelve critical entries of the Patch Tuesday; the lines of Node.js 22, PostgreSQL 17 and
MySQL 8.0 remove 5, 5 and 6 hits). All are open and raised one by one by the forward search; the acceptance of phase 5a
(at most 25 for a stack of ten entries) holds with 19 at most. 6 452 records, 376 s.

**Backfill.** The same run sets up the four ten-entry stacks a second time after the week, so the reverse search brings
the week at once: 14, 4, 19 and 3 notices, the same ones, all quiet (`is_backfill`, listed as «earlier»), one
`notice.backfilled` each and no `notice.raised`. The spike has one week; the look-back holds about 26. If every week
looked like this one (it held a Patch Tuesday, a PostgreSQL and a Chrome release), a stack set up today would bring 80 to
500 earlier notices; that is why they are not counted as new and get a single summary.

In CI, the spike-week test of the reference set adds a second organisation whose stack names the three example projects'
technologies without versions: 5 notices (1, 3 and 1, as ADR 044 measured on the full week), no reminder.

## Alternatives

- **No finding for a coarse hit, the notice alone.** Loses the evidence the line narrowing and the reference set need, and
  the answer «why no reminder» on the alert.
- **Withdrawing a notice whose score dropped below the threshold.** A notice would come and go with every rescoring;
  once asked, the question stays until it is answered or nothing names it.
- **Undo only within a few seconds.** See decision 8.
- **A version given with «affects us» on a separate component next to the stack entry.** Every later entry would raise a
  notice and a finding for the same product.
- **One `notice.raised` per notice also in the reverse search.** Adding Windows Server to a stack would announce up to half
  a year of critical entries one by one.
- **Reopening a person's closed finding on the coarse path by the rules.** It would flap: the rules' reopening hands the
  finding to the system, which returns it to the notice.

## Consequences

- 5a.5 builds the Notices page on these endpoints; the overview's «n new notices» counts the open notices that are not
  backfilled (`state=new`). A reverse search can bring many quiet notices at once (half a year for a new stack entry); the
  page shows them as «Earlier» (`state=earlier`) and may group them by stack entry.
- 5a.6 wires `notice.raised` (and, as one message, `notice.backfilled` and `notice.reappeared`) to the notifications.
- 5a.4's stack editor writes components; a threshold or line changed there queues the reverse search, which updates the
  notices quietly.
- The release gate of ADR 046 decision 13 stays until 5a.5 is merged.
