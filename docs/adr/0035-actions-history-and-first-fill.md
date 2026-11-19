# ADR 035: Actions, history rows and the first fill

- Status: accepted
- Date: 2026-11-19
- Refines: ADR 011 (the action as unit of work), ADR 014 (notification state per action)
- Master plan: 4.6, 4.9, 6.7, 7.4, 7.7, 8.2, 10.2, task 3.5

## Context

Master plan 7.4 and 4.9 define the action (all open findings of one component in one project), its level,
target, deadline and status. 8.2 lists the events, each identified by its history row, and 7.7 asks that a
new project, or a newly connected repository, get one summary instead of single alarms. Left open: when
actions are brought in step with their findings, how a first fill is recognised, how events and history
relate, and the shape of the read API that the overview, list and details (tasks 3.8 to 3.10) build on.

## Decision

1. **One step at the end of each unit of work.** The assessment writes findings per alert; the components
   whose findings changed are collected and the actions of exactly those components are brought in step
   once, at the end of the job or request (`Remediations.SyncAsync`), under an advisory lock per
   component taken in id order. A run through hundreds of entries thus raises an action once, at its final
   level, instead of raising and lifting it entry by entry.
2. **Events are history rows.** `assess.assess_event` holds both the events of 8.2 (`action.*`,
   `backfill.completed`) and history-only rows (`finding.joined`, `finding.closed`, `finding.reopened`,
   `finding.status_changed`). The row's id is the event's id for the notifications (phase 5). An action
   closed by the system (fixed by update, withdrawn entry, removed component) writes `action.withdrawn`
   with the reason; one closed by people writes `action.status_changed`.
3. **A first fill is a first look.** `assess.match_scope` records which projects and which repository
   links the reverse search has seen. A run over a project not seen yet, or over components of a link not
   seen yet, marks the new findings `is_backfill`, opens their actions without `action.raised` and ends
   with exactly one `backfill.completed` for the project (counts and highest level in its data). A project
   is only marked seen once it has components. The forward search leaves components of an unseen project
   or link alone and queues that project's reverse search, so a wave of entries arriving during setup
   cannot bypass the first fill.
4. **Numbers.** `assess.org_counter` is raised with `INSERT … ON CONFLICT DO UPDATE … RETURNING` in the
   writing transaction, which holds the row lock until commit. Actions get their number when they open,
   alerts when one of their findings first reaches level 2.
5. **People's decisions.** Status changes always name the status the caller saw (`expectedStatus`); a
   mismatch is `409 status_conflict` (master plan 10.1). `open` and `in_review` are set on the action and
   apply to its unfinished findings; outcomes are set per finding or for all at once. A person's
   «not affected» is an exclusion and gives level 1. A reassessment never overwrites an outcome a person
   set; it reopens the finding only when the level the rules give without that decision rises above the
   level at closing, or the version check says «in range» again after it did not (master plan 6.7). The
   finding then joins the open action of its component or opens a new one. Taking a closing back
   (`open` on a closed action) reopens the findings people closed, unless a newer action for the
   component is open.
6. **Deadlines.** Start plus the rule set's default deadline for the level; the start is the opening or
   the last raise. A deadline a person set stays until they reset it.
7. **Read API.** Under `/orgs/{orgId}`: `overview`, `actions` (filters `status`, `level`, `project`, `q`;
   cursor pages, highest level first), `actions/{number}` with findings and history, `PATCH` for
   assignment and deadline, `actions/{number}/status`, `…/events`, `…/notes`, `findings` (including level 1),
   `PATCH findings/{id}`, `alerts` and `alerts/{number}`. Component and project names come from Assets
   at read time, entry titles from Intel; only the entry key is kept on the alert, for the search.

## Consequences

- The notification module (phase 5) reads `assess_event` rows of the 8.2 kinds by id; nothing is pushed yet.
- Organisation-specific deadlines (master plan 7.6) and a person's override of the development cap are not
  stored yet; both have their inputs in the rule engine.
- `assessment` (immutable assessments with model output) and `assessment_feedback` come with phase 4.
