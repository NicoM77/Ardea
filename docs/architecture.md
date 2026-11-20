# Architecture

A short map of how Ardea is built: the shape of the program, its modules and the rules between them. The reasons
are in the [architecture decision records](adr/README.md). The full description is still chapter 3 of the master
plan (German); task 7.12 moves what still applies into this page before the master plan is deleted.

## Shape

- **A modular monolith with a separate worker.** One program in one container image, started in one of three
  roles: `api` (the HTTP endpoints, the [OpenAPI document](../openapi/ardea.v1.json) and the web app), `worker`
  (the jobs: fetching sources, assessing, notifying) and `migrate` (database migrations, with a dump before them,
  [ADR 028](adr/0028-runtime-image-and-pre-migration-dump.md)).
- **PostgreSQL 18.** Each module owns its own database schema and `DbContext`. Tenant isolation is an access check
  and a query filter, with row-level security as hardening ([ADR 029](adr/0029-row-level-security-as-hardening.md)).
- **Jobs in the database.** A job table read with `SKIP LOCKED` runs the scheduled and follow-up work and serves as
  the outbox; one unit of work spans every module a job touches.
- **The web app** (`web/`) is an Angular application with its own component library, built into the image and
  served by `api`.

## Modules

| Module | Responsible for |
|---|---|
| Source adapters (`src/Sources/`) | One adapter per source: fetch and translate into observations. They know only the provider SDK, which is licensed under Apache-2.0 so adapters can be written under any licence ([Add a source](operations/add-a-source.md)). |
| Intel | Fetch control, raw records with revisions, normalisation, merging into one entry per vulnerability, change history, signals (KEV, EPSS). |
| Catalog | The technology catalogue: products, aliases, product keys, version schemes ([ADR 030](adr/0030-catalogue-format-and-master-data.md), [ADR 040](adr/0040-catalogue-that-learns.md)). |
| Iam | Users, sign-in and MFA, organisations, memberships, invitations, roles. |
| Assets | Projects, the stack and its components, the setup wizard, GitHub access and repository profiles ([ADR 032](adr/0032-github-sbom-export.md)). |
| Assessment | Candidate search against the profiles, the rule set and the level, the optional assessment by a language model, notices and reminders ([ADR 033](adr/0033-candidate-search-and-version-verdicts.md), [ADR 034](adr/0034-rule-set-file.md), [ADR 037](adr/0037-assessment-by-language-model.md), [ADR 047](adr/0047-notice-generation-and-answers.md)). |
| Notifications | Rules, recipients, the four channels (in-app, e-mail, Web Push, webhooks), delivery and digests ([ADR 038](adr/0038-notifications.md)). |
| Platform (`src/BuildingBlocks/`) | Jobs and schedules, tenant context and unit of work, audit, settings, secret protection, telemetry. |

## Rules between modules

1. A module uses another only through that module's `Contracts` assembly; everything else is `internal`.
2. No module reads or writes the tables of another module.
3. Dependencies point one way: Catalog and Iam use no other module; Intel uses Catalog; Assets uses Catalog and
   Iam; Assessment uses Intel, Assets, Catalog and Iam; Notifications uses Assessment, Iam and Intel. Cycles are not
   allowed. A stated exception is recorded in its ADR ([ADR 047](adr/0047-notice-generation-and-answers.md)).
4. Source adapters know only the provider SDK: no database, no tenant data, no other adapter.

Architecture tests in the build enforce these rules. The rules for code, dependencies and tests are in
[`CONTRIBUTING.md`](../CONTRIBUTING.md).
