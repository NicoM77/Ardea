# ADR 046: The stack, version precision, notices, and reminders without assignment or deadline

- Status: accepted
- Date: 2026-11-19
- Refines: ADR 011 (the action as unit of work), ADR 035 (actions, history rows and the first fill), ADR 044 (notices per
  week and thresholds); master plan 4.5, 4.6, 4.9, 17.2, 17.3, 17.5, 17.6
- Master plan: 4.11, 9.2, 14 (phase 5a), 17, task 5a.2

## Context

Chapter 17 makes the profile two-tiered: a coarse **stack** of the organisation and precise projects. A component
without an exact version raises **notices** («could affect you», yes or no) instead of findings and actions; the action
becomes a **reminder** without assignment, deadline or «in review» (17.5). Task 5a.2 lays the data model for this and
removes what the reminder no longer has; generating notices is task 5a.3, the wizard 5a.4, the pages 5a.5. ADR 044 set
the thresholds. Open: how the stack is stored and exposed, how precision is decided for components that exist, what the
notice table holds, what happens to open actions on components without a version (E25), and how much renaming happens now.

## Decision

1. **The stack is a system project.** `assets.project.kind` is `project` (default) or `organization_stack`; a partial
   unique index allows one stack per organisation. Assets subscribes to «organisation created» (the hook the notification
   rules use) and creates the stack, named «Stack», in the same unit of work. The module's master-data step, which runs
   after every `migrate`, gives every organisation without one its stack (idempotent): the organisations from before,
   and those the previous API creates during the upgrade, before the hook exists, get theirs on the next migrate. Its
   entries are ordinary components: same endpoints
   (`/projects/{stackId}/components`), same search keys, same candidate search (17.2).
2. **How the stack is exposed.** `GET /orgs/{orgId}/stack` returns the stack's project id and its entries. The project
   list leaves the stack out (also with `archived=true`), the org-wide coverage hints leave it out (its missing versions
   are intended; asked for by id they are given), `PATCH` and `DELETE` on the stack answer `409 stack_project`, and so
   does connecting a repository to it (a repository makes a project precise, 17.4). `GET /projects/{stackId}` still reads
   it; the wizard and the pages come with 5a.4 and 5a.5.
3. **Version precision is stored and may be stated.** `assets.component.version_precision` is `none`, `line` or
   `exact`; a check constraint keeps `none` exactly where there is no version. The API takes `versionPrecision` with a
   component and, when it is left out, derives it (`VersionPrecisions.Of`; the migration backfills by the same rule):
   no version → `none`; a package (purl) → `exact`; a platform read from a repository with one or two numbers («10.0»,
   the existing «version line only» hint) → `line`; a version typed by hand with one number («17») or ending in a
   wildcard («8.x», «8.3.*») → `line`; anything else → `exact`. A hand-typed «8.3» stays `exact` because many products
   count releases with two numbers (PostgreSQL 17.11) and today's components keep their precise path; whoever means the
   line says so. A new version without a stated precision gets the derived one; a repository component's precision
   follows its repository like its version. Versions are judged and stored without surrounding blanks; SQL
   (`assets.version_precision_of`, the check constraint) calls blank what C# trims (`[[:space:]]`).
4. **The threshold is stored per component.** `assets.component.notice_threshold` is `only_important` (default) or
   `everything` (ADR 044 decisions 2 to 4), set with `noticeThreshold` on the component endpoints. It is stored on every
   component because the depth hangs on the component (17.1); 5a.3 evaluates it for coarse ones.
5. **`assess.notice`.** One row per organisation and entry (`ux_notice_entry`): `entry_key` (as on the alert, for lists),
   `state` (`open`, `affects_us`, `not_affected`), `component_ids` (the coarse components that raised it: the reason
   «Your stack names PostgreSQL»), `decided_by` and `decided_at` (required once answered), `remediation_id` (the reminder
   «affects us» opened, `SET NULL`), `reappeared_at` and `reappeared_reason` (`exploited`, `kev`; set once, so a notice
   reappears at most once, E22), `raised_at`, `changed_at`. Row-level security like every organisation table; the
   tenant isolation test covers it through its schema. No endpoint yet: 5a.5 shapes the read side with the page, and the
   migration is tested against the database.
6. **Reminders are open or done.** The action's status is `open` or `closed` (17.5's «done»; the stored value keeps its
   name, decision 11) and a finding's `open` or an outcome; «in review» is gone. The migration sets every action and
   finding in review to `open`. `PATCH /actions/{number}` (assignment and deadline) is removed; `ActionSummary` loses
   `assignedTo`, `dueAt` and `overdue`, the overview `overdue`; the list filters `status=in_review` no more (`active`
   stays as a synonym of `open`); the most urgent actions sort by level, then by how long they have stood at it. The
   event `action.assigned` is no longer written or announced; rules no longer name it (notify migration
   `WithoutAssignment`), a rule that named nothing else is deleted with its targets (it would match nothing and its
   empty event list could not be saved again; disabling it would leave the same unsaveable rule listed), and its in-app
   notifications leave the inbox. The rule set file drops `deadlineHours`; its
   levels do not change, so its version stays 1, and a data folder of the previous version that still names them loads
   for one version with them ignored. The web pages lose the owner, the deadline, the runway, the countdown,
   «Take into review», «Give back», the owners card and the time to close (17.5 drops «Dauer bis Abschluss» too).
7. **The previous version keeps running against this schema during the upgrade (4.11).** `assess.remediation.assigned_to`,
   `due_at` and `due_at_manual` are no longer mapped, written or read; `due_at_manual` gets the default `false` so
   inserts need not name it. The narrowed rules stay valid for the old writers through two triggers of this version
   only: `assets.component_fill_precision` derives `version_precision` when a writer leaves it unfitting its version (the
   old API and worker insert versioned components without it), and `assess.open_instead_of_review` stores a status
   `in_review` the old API writes as `open`. The check constraints (`none` exactly without a version; status `open` or
   `closed`, findings without `in_review`) therefore hold from this version on. The next version's cleanup migration
   drops the two triggers and their functions together with `assigned_to`, `due_at` and `due_at_manual`, and the rule
   set's tolerance of `deadlineHours` (decision 6).
8. **History written before stays, but reads like today.** History is never deleted (4.10). Reads (the action's history
   and the events handed to the notifications) leave out `action.assigned` rows and the taking into review or giving
   back; elsewhere «in review» reads as «open». The overview's «last 24 hours» counts leave out the same rows and the
   migration's own closings (`moved_to_notice`), so the upgrade shows no burst.
9. **Open actions without a version become notices (E25).** The migration `NoticesAndOpenOrDone` takes every action that
   is not closed and whose component has precision `none`: each entry of its open findings becomes an open notice (one
   per organisation and entry, naming every such component, raised when the oldest of those actions opened). The
   findings close as `not_affected` with the reason `moved_to_notice` at level 1 (factors with the exclusion
   `moved_to_notice`), the action closes with the close reason `moved_to_notice`, both histories say so
   (`finding.closed`, `action.withdrawn`; rows written by a migration reach no notification), and alert levels are
   recomputed. Closing rather than deleting keeps numbers, history and the answer to «what happened to #143». Closed
   actions and actions on components with a line or an exact version stay unchanged.
   **Until 5a.3 routes coarse components to notices,** the assessment keeps a finding the system closed with the open
   notice that names its component: whatever reason a later closing wrote (archived and unarchived, no longer matched,
   withdrawn and republished, a catalogue change), it returns to `not_affected` / `moved_to_notice` at level 1 instead of
   opening an action, as long as the component has no version and the entry is not actively exploited. With a version
   entered it takes the precise path again; with active exploitation (KEV included, `exploitation = active`) it opens an
   action again, the analogue of E22, because a moved finding is otherwise invisible until the Notices page exists. A
   finding a person reopened or closed follows the person's rules as before.
10. **Cross-schema reads and their order.** Two steps read another module's schema in SQL: the Assets master-data step
    inserts stacks for the ids of `iam.organization`, and the Assess migration `NoticesAndOpenOrDone` reads
    `assets.component.version_precision`. Both depend on the fixed module order of `migrate`
    (`src/Hosts/Ardea.Host/Modules.cs`, master plan 4.11: platform, iam, catalog, assets, intel, assess, notify; master
    data after every schema). A test database that holds a schema without the other one skips the step (`to_regclass`);
    when `assets.component` exists without `version_precision`, the Assess migration raises an error instead of
    skipping E25 silently. Changing the module order must keep Iam before Assets before Assess.
11. **Naming churn stays low.** Code, tables and API keep «action» (`assess.remediation`, `/actions`,
    `ActionSummary`) and the status value `closed`; the UI wording «Reminder» and the Notices page come with 5a.5.
12. **Phase 5a acceptance, «no endpoint knows assignment or deadline».** A test walks the committed OpenAPI document
    (which another test keeps equal to the code) and fails on any schema property, parameter, enumeration value, path or
    schema name that names an assignment, a due date, a deadline, «overdue», «remaining» or «in review» («assigner», who
    scored a CVSS vector, is not one).
13. **Release gate: no release between 5a.2 and 5a.5.** After this migration, notices exist but nothing shows them
    until the Notices page (5a.5), and new entries on components without a version still open actions until 5a.3. An
    instance must therefore not be released or upgraded to a build of this phase before 5a.3 and 5a.5 are merged; the
    README's phase 5a section says so.

## Alternatives

- **The stack as its own table.** Rejected by 17.2: components, search index and candidate search would need a second
  path.
- **Derived precision only (a generated column).** Cannot tell «PHP 8.3» (a line) from «PostgreSQL 17.11» (a release);
  the wizard needs to state it.
- **Treating any one- or two-number version as a line.** Would move every hand-typed «17.11» to the coarse path at
  migration time.
- **Deleting the migrated actions.** Loses numbers and history; findings would open a new action at the next touch.
- **Renaming `closed` to `done` and `action` to `reminder` now.** Touches every layer twice; done once with the pages
  in 5a.5.
- **Dropping `assigned_to` and `due_at` now.** Breaks the compatibility rule of 4.11 for an API of the previous version
  running against the new schema.
- **Narrowing the checks without triggers, or keeping them wide for a version.** The old build's writes would fail
  (23514) or leave versioned components at `none` and actions in review; the triggers keep the data in the new shape
  from the first day.
- **Basing the guard on the reason `moved_to_notice`.** Any later closing overwrites the reason, and archiving and
  unarchiving a project, a withdrawn and republished entry or a catalogue change would reopen the finding.

## Consequences

- 5a.3 writes `assess.notice` from coarse components (`version_precision` `none` or `line`) with the stored threshold,
  turns «affects us» into a reminder (`remediation_id`) and fills `reappeared_*`; it also decides what becomes of
  findings on coarse components that the precise path still opens until then.
- 5a.4 lets the wizard state precision and threshold; 5a.5 renames the action to «Reminder» in the UI, adds the Notices
  page and its read API, and may expose the project kind.
- The next version's cleanup drops `assigned_to`, `due_at` and `due_at_manual`, the triggers
  `component_fill_precision` and `*_open_instead_of_review` with their functions, and the tolerance of `deadlineHours`.
- An open notice is not withdrawn when its component later gets a version: the finding then opens an action and the
  notice still asks. Withdrawing or answering such notices is 5a.3's job, as is the guard of decision 9, which 5a.3
  replaces by routing coarse components to notices.
