# ADR 029: Row-level security as hardening (experiment 0.6)

- Status: accepted
- Date: 2026-11-18
- Refines: ADR 012 (tenant separation), ADR 023 (one transaction per unit of work)
- Master plan: 2.8, 9.2, task 0.6, decision E5

## Context

Tenant separation rests on two mandatory layers: the access check at every endpoint and the EF Core query
filter on every organisation-owned entity. Row-level security in PostgreSQL was planned as a third layer, but
no primary source described how it behaves with EF Core, Npgsql's connection pool and a transaction shared
by several module contexts. The questions were: transaction or session scope for the tenant setting, reset
when a connection returns to the pool, and whether two `DbContext`s in one unit of work see the same tenant.

## Experiment

`RowLevelSecurityExperimentTests` (PostgreSQL 18.7, Npgsql 10.0.3, EF Core 10.0.15). A table with a policy on
`organization_id = nullif(current_setting('ardea.org_id', true), '')::uuid` for `USING` and `WITH CHECK`,
used by the application role, which owns nothing and has no `BYPASSRLS`. The unit of work sets
`ardea.org_id` and `ardea.user_id` with `set_config(…, true)` as its first statement, i.e. transaction-locally.
Every query bypasses the EF filter, so only the database separates tenants.

| Scenario | Result |
|---|---|
| 40 units of work for two organisations in parallel on a pool of three connections | each saw only its own rows |
| A unit of work without organisation on the same physical connection right after another organisation's commit (pool of one) | saw nothing; an insert for the previous organisation failed with `42501` |
| Insert of a row for another organisation | rejected by `WITH CHECK` (`42501`) |
| Two module contexts in one unit of work | the second saw the first's uncommitted row with the same tenant |
| Rolled-back unit of work | left nothing behind |

## Decision

1. Row-level security becomes the third layer, as hardening, not as foundation (decision E5). It is enabled with
   task 2.2 on every organisation-owned table, by migration, with the policy above; user-owned tables get the
   analogous policy on `ardea.user_id`.
2. The tenant setting is **transaction-local only**. Nothing is set per session, so nothing has to be reset
   when a connection goes back to the pool; a connection without a fresh transaction-level setting sees nothing.
3. System operations that must read across organisations (master plan 9.2, closed list) become narrow
   `SECURITY DEFINER` functions owned by `ardea_migrator`, returning identifiers or counts only. A new one
   needs a new function and an ADR.
4. The owner role is not subject to the policies (no `FORCE ROW LEVEL SECURITY`): maintenance commands such
   as export and delete of an organisation run as owner by design.

## Consequences

- A missing `WHERE` in hand-written SQL or a forgotten filter in a future module returns nothing instead of
  another organisation's data.
- The cross-organisation access test (task 2.2) still runs against the API; RLS does not replace it.
- `current_setting` is evaluated per row; with an index on `organization_id` the cost is negligible at the
  planned sizes. This is to be watched in the query plans of task 6.4.

## Implementation (task 2.2)

- Policies use `platform.current_organization_id()` and `platform.current_user_id()`, which read the
  transaction-local settings. Organisation tables (`iam.organization`, `iam.membership`, `iam.invitation`,
  `platform.audit_event`) are filtered by organisation. User tables (`iam.user`, `iam.user_claim`,
  `iam.user_login`, `iam.user_token`, `iam.session`) are filtered by user.
- The user rules of master plan 9.2 are part of the policies. A user sees their own memberships and those
  organisations before choosing one. Members of the current organisation see each other's account row,
  but nobody can write another account.
- Instance audit events (no organisation) can be written from any unit of work; only the owner role
  reads them.
- Flows that start without a user (sign-in, two-factor step, registration, setup, password reset,
  e-mail confirmation, account e-mail jobs, the `reset-password` command) first find their account and
  make it the user of their unit of work before it begins. The audit actor of these flows is that account.
- The system functions so far (`SECURITY DEFINER`, owned by the migrator, `search_path` pinned). Each
  returns identifiers, a flag or a count:

  | Function | Flow (master plan 9.2) |
  |---|---|
  | `iam.user_id_by_email(email)` | sign-in: the account of an address |
  | `iam.validate_session(session, user, stamp, idle)` | sign-in: the session check in front of every request |
  | `iam.any_user()` | instance figure: whether setup is still open |
  | `iam.invitation_organization(token_hash, email)` | accept an invitation: the organisation of a token |
  | `assets.repository_link_counts()` | instance figures: repository links per state, counts only |
  | `assets.organizations_with_component_keys(types, values)` | candidate search: the organisations whose components carry these keys (ADR 033) |
  | `assess.organizations_with_alert(entry)` | reassessment, withdrawal of an entry: the organisations with an alert for it (ADR 033) |
  | `assess.alerts_to_reassess()` | `ardea admin assess-recompute`: the pairs of organisation and entry that have an alert (task 3.11) |
  | `assess.model_tokens_since(since)` | token quota of the instance: the language model tokens of all organisations since a point in time, a sum only (ADR 037) |

- `TenantIsolationTests` checks that every table has exactly one kind and the matching policy, and calls
  every organisation endpoint with foreign organisations and foreign object ids.
