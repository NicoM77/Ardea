# ADR 043: Instance warnings mailed to the instance administrators

- Status: accepted
- Date: 2026-11-19
- Refines: ADR 023 (one transaction per unit of work), ADR 029 (row-level security)
- Master plan: 12.7, task 6.6

## Context

Chapter 12.7 asks that the system warns about itself when it no longer works reliably: a source not fetched for six
hours, a stalled worker or permanently failing deliveries create a notification to the instance administrator and a
hint on the overview. The warnings existed (`IInstanceStatusContributor` per module: `worker_stalled`,
`source_stale`, `notify_events_failed`, `deliveries_failing`) and were shown on the overview and in the
administration, but they were computed only when someone looked. Nobody was told.

Three things constrain the design. A stalled worker cannot report itself, so the check cannot be a job. The
instance administrators' addresses are user data of Iam behind row-level security, and the check runs without an
organisation and without a user. And no transaction may stay open while the mail server is called (ADR 023).

## Decision

1. **Self-check in both roles.** A hosted service runs a round every five minutes in `api` and in `worker`; the
   first round waits one interval, so that a worker started together with the API has beaten by then. One process
   at a time does a round: a session advisory lock (`pg_try_advisory_lock`) on a connection of its own, taken with a
   try-lock and released at the end; a process that does not get it skips the round. The job queue plays no part.
2. **State per warning.** `platform.instance_warning` (instance data, no organisation, no row-level security), keyed
   by code and subject (empty when the warning has none): severity, message, `first_seen_at`, `last_seen_at`,
   `cleared_at`, `notified_at`, `reminded_at`, `resolved_at`. A round computes the status through the existing
   contributors and reconciles the rows (`InstanceWarningRules`, pure, with `InstanceWarningLedger` around it):
   - a warning without a row, or one whose «resolved» mail went out, is **new**: it is claimed for the mail at once
     (`notified_at`);
   - an active warning is claimed again **24 hours after the last mail** about it (`reminded_at`), and again every
     24 hours while it lasts;
   - a warning the round no longer sees gets `cleared_at`; it counts as **resolved** only when a later round, at
     least four minutes on (in practice the next one), still does not see it (`resolved_at`). A warning that comes
     back in between simply goes on, without a second «new» and without a «resolved» mail. A row whose «resolved»
     mail went out is removed by the next round; a warning nobody was told about leaves without a mail.
   Two rounds a moment apart (one per role) change nothing: the rules depend on times, not on counting rounds.
3. **One mail per round and administrator.** Every instance administrator with a confirmed address gets one plain
   mail that bundles everything of the round: the new warnings, the lasting ones, the ones that are over. It holds
   only what the status already shows (the message, since when it is reported), a link to the administration
   (`PUBLIC_URL/admin`) and a footer saying why it was sent; no secret, no profile contents, no figures. Without
   SMTP, or without an administrator with a confirmed address, nothing is claimed and nothing fails: the state is
   kept, the warnings stay on the overview and in the administration, and the process says so once in its log
   (information level). A later round with a mail server tells the administrators about what is still active.
4. **No transaction during SMTP.** Who can be told is read first, in short units of work of their own (the sender
   reads its settings, the directory below reads the accounts). Then one short unit of work computes the status,
   reconciles the state and claims what to mail, and commits; the mail goes out after that, without a transaction.
   If sending fails for any recipient, a new short unit of work gives the claim back (`notified_at`, `reminded_at`
   or `resolved_at` as before the round), so the next round tries again for everyone: a duplicate mail is preferred
   to a lost warning. A process that stops between commit and send loses that mail; the warning stays visible.
5. **Recipients through Iam's Contracts.** `IInstanceAdministrators.ListReachableAsync` returns the user id, name
   and confirmed address of each instance administrator. It does not add a function that hands out addresses: the
   new system function `iam.instance_administrators()` (`SECURITY DEFINER`, owned by the migrator, `search_path`
   pinned) returns **ids only**, and each account's row is then read in a short unit of work that acts as that
   account, which the existing user policy allows (`id = platform.current_user_id()`). This is the pattern of the
   account e-mail jobs (ADR 029, «flows that start without a user»).

   | Function | Flow (master plan 9.2) |
   |---|---|
   | `iam.instance_administrators()` | self-check: the accounts with the instance role, to mail an instance warning to; ids only |

6. **Administration.** `GET /api/v1/instance/status` adds `notified`: per active warning the administrators were
   mailed about, the time of the last mail. The card *Operation* shows «Administrators mailed …» under the warning.
   No setting: who gets the mail follows from the instance role and the confirmed address.

## Alternatives

- **A scheduled job.** Rejected: the job queue is what stalls when the worker stalls; the warning that matters most
  would never be sent.
- **A policy that lets a unit of work without user read the instance administrators' rows.** It would need a new
  marker for «system without user» in every user policy and widen what such a unit of work sees; reading each
  account as itself needs nothing new in the policies.
- **A function that returns the addresses.** Rejected by ADR 029: system functions return ids, flags or counts.
- **Mail only after a warning was seen in two rounds.** Would spare a mail for a worker restart that takes longer
  than two minutes, but delays every first warning by five minutes; the existing thresholds (two minutes for the
  worker, six hours for a source, a dead delivery) already filter short blips. Not done; to revisit if restarts
  cause mails in practice.

## Consequences

- An instance without SMTP behaves as before, with one information line in each process's log.
- A failure at one administrator's mail server repeats the mail for the others in the next round.
- `TenantIsolationTests` lists `platform.instance_warning` as global; ADR 029's table of system functions gains
  `iam.instance_administrators()`.
- Tests: the rules (`InstanceWarningRulesTests`, without database) and the rounds against PostgreSQL
  (`InstanceSelfCheckTests`: one bundled mail to every administrator and none to others, nothing in the next round,
  reminder after 24 hours, «resolved» only after a clear round, no SMTP, two processes at once, a stalled worker
  reported by the API role without the job queue, a failed mail retried by the next round).
