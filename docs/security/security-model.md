# Security model

A technology profile is a map of an organisation's attack surface. An instance that holds the profiles of several
organisations treats their separation and protection as a core function. This page describes the model; the
[threat review](threat-review.md) goes through every threat with its mitigation in the code and the test that proves
it, and [`SECURITY.md`](../../SECURITY.md) says how to report a vulnerability.

## Tenants and roles

```
Instance
 ├─ Users (one account, any number of memberships)
 ├─ Sources, entries, catalogue (global, the same for everyone)
 └─ Organisation
     ├─ Memberships with a role
     ├─ Stack and projects → components, repository links
     ├─ Alerts → findings, assessments, history; notices; reminders
     └─ Rules, notification state, deliveries, webhook targets
```

The active organisation is part of the address: `/o/{slug}/…` in the web app and `/api/v1/orgs/{orgId}/…` in the
API, never a hidden session state. Roles apply per organisation:

| Right | Owner | Admin | Member | Viewer |
|---|:-:|:-:|:-:|:-:|
| Read reminders, notices, alerts, projects, overview | ● | ● | ● | ● |
| Answer notices, set statuses, add notes, correct assessments | ● | ● | ● | |
| Maintain projects and components, link repositories, accept suggestions | ● | ● | ● | |
| Add and delete GitHub accesses | ● | ● | | |
| Notification rules, webhook targets, external addresses, reminder rhythm, delivery log | ● | ● | | |
| Invite members, change roles, read the audit log | ● | ● | | |
| Organisation settings (AI data policy, MFA requirement) | ● | ● | | |
| Transfer ownership | ● | | | |

The first account of an instance is also **instance administrator**: it manages sources, language model settings,
SMTP, registration and GitHub Enterprise Server hosts, but sees no organisation's data by that role. Exporting or
deleting an organisation is a maintenance command of the operator (`ardea org export`, `ardea org delete`).

Registration is set per instance: open, by invitation (the default) or closed. A fresh instance accepts its first
account only with the **setup token** from the log or `ARDEA_SETUP_TOKEN`, so nobody can take over an instance that
was just started and is already reachable.

## Tenant isolation

Every table is global, organisation or user data ([Data model](../architecture/data-model.md#three-kinds-of-data)),
and each kind has its access rule:

| Data | Who reads and writes it |
|---|---|
| Organisation | only with a tenant context: in the API from the checked membership, in the worker from the job's organisation |
| User | the account itself; members of the same organisation see each other's name, e-mail address and role; the worker delivers to a named recipient within its job's organisation; sign-in finds an account by its e-mail address before a user context exists |
| Global: entries, sources, catalogue | read by every signed-in user; written by the worker, sources also by the instance administrator |
| Global: jobs, schedules, instance settings | the worker and the instance administrator only |

Three layers enforce it ([ADR 029](../adr/0029-row-level-security-as-hardening.md)):

| Layer | Mechanism | Protects against |
|---|---|---|
| Access | Every endpoint under `/orgs/{orgId}` checks membership and role before a handler runs. Ids in request bodies are resolved within the tenant; a foreign id is «not found». | calls with a foreign organisation or object id |
| Query | Named EF Core query filters on every organisation and user entity; without a tenant context the `DbContext` throws. | a forgotten `WHERE` |
| Database | Row-level security on the same tables, with the organisation and user set transaction-locally at the start of every transaction. The application connects as `ardea_app`, which owns no table and has no `BYPASSRLS`; `ardea_migrator` owns the tables and is used only by `migrate` and the maintenance commands. | a mistake in the second layer, raw SQL, future queries outside EF |

The first two layers, with an automatic cross-tenant test of every organisation endpoint (also with foreign ids in
the request body), are the foundation; row-level security is hardening against programming mistakes, not against an
attacker who can already run arbitrary SQL.

**System functions.** Some flows must ask across organisations: the candidate search, reassessment, warnings,
digests and reminder runs, due repositories, sign-in, accepting an invitation, instance figures, the self-check.
Each question is a narrow `SECURITY DEFINER` function that returns only identifiers, flags or counts; reading and
writing then happen in jobs with a tenant context. The list is closed: a new function needs an ADR, and a test fails
for any function not on it. The list is in [ADR 029](../adr/0029-row-level-security-as-hardening.md).

## Authentication

| Topic | How |
|---|---|
| Basis | ASP.NET Core Identity with Ardea's own endpoints on `SignInManager` and `UserManager` |
| Browser session | Cookie `__Host-ardea` (`HttpOnly`, `Secure`, `SameSite=Lax`), 12 hours sliding, 14 days absolute, backed by a server-side session that can be listed and revoked. Every sign-in starts a new session. API endpoints answer 401 or 403 instead of redirecting. |
| Ending sessions | Changing the password or MFA, a password reset and «sign out everywhere» end every session through the security stamp. |
| CSRF | Antiforgery token in a header on every changing request, plus checks of `Sec-Fetch-Site` and `Origin` |
| Password | At least 12 characters, no composition rules, checked against a list of common passwords; hashed by Identity (PBKDF2) |
| Two-factor | TOTP with recovery codes. An organisation can require it of its members. Each code works once per account, also under concurrent requests ([ADR 045](../adr/0045-single-use-authenticator-codes.md)). |
| Abuse | Lockout after repeated failures, rate limits per client network on every sign-in, setup, registration and reset step, and the same answer whether an account exists or not |

Passkeys, OIDC sign-in and API tokens are planned after 1.0 (see the [README](../../README.md#after-10)).

## Secrets and sensitive data

| Data | Protection |
|---|---|
| Secrets: SMTP password, webhook URLs and secrets, the Web Push private key, GitHub tokens, authenticator keys | Encrypted in the database with ASP.NET Core Data Protection; its key ring is in the database, protected by `ARDEA_MASTER_KEY` (or a secret file) kept outside it. Never in API answers or logs. |
| Language model API keys | Only in the environment, never in the database |
| Profiles, read dependencies and stored SBOMs, alerts, assessments | Tenant isolation; no contents in logs or telemetry; source code is never stored |
| Accounts | Identity's storage; authenticator keys encrypted as above |
| Sources, entries, catalogue | Public data |

A database dump without the master key exposes profiles but no secrets; the
[installation](../operations/installation.md) says to keep the master key apart from the dumps and to put volumes and
backups on an encrypted disk.

The instance sends nothing to the project or its developers. Telemetry goes only to an endpoint the operator sets.
The audit log (`platform.audit_event`) records sign-ins and account changes, memberships and roles, and changes to
profiles, rules, settings and statuses, each with actor, time, organisation and origin.

## Untrusted content

| Risk | Measure |
|---|---|
| Script in advisory texts | The web app never binds data as HTML; links from source data open only when they are `http` or `https`; a strict Content Security Policy without inline script. E-mails encode every untrusted text. |
| Instructions to the language model in source texts | The model has no tools; source texts travel in a data block; the answer must match a fixed schema and name only what it was given; the model cannot exclude a finding, lowers a level by at most one, and its «not affected» is a proposal a person confirms ([Rule set and levels](../architecture/rule-set.md#language-model)). |
| Manipulated or oversized source answers | Adapters call only their declared hosts over HTTPS; size and time limits per response; bounded unpacking of archives. |
| Manifests, lock files and SBOMs from repositories | Read, never executed: no build, no install, no script. Limits on number and size of files; XML is read without DTDs. |
| A compromised source | Precedence rules between sources and the provenance of every field, visible in the interface |

## Outbound connections

The instance calls out to the sources, GitHub, the language model providers, SMTP, push services and webhooks. Every
connection that a user or an organisation can point somewhere goes through one guard (`OutboundAddresses`): the host
is resolved once and every address is checked on the actual connection.

- Loopback, link-local (cloud metadata), unspecified and multicast addresses are never reachable.
- Private ranges only for hosts the instance administrator released (`ARDEA_WEBHOOK_INTERNAL_HOSTS`).
- Webhooks and push follow no redirect; push subscriptions are accepted only for the browser makers' push services
  and those the operator adds.
- Sources follow redirects only to their declared hosts. GitHub requests go only to the fixed API and SBOM download
  hosts, or to the Enterprise Server hosts the instance administrator registered, and carry the token only to the
  host it belongs to.
- Language model providers are set by the operator and may be internal, but follow no redirect.

The settings are in [Configuration](../operations/configuration.md).

## Hardening

- **Headers** on every response, errors included: Content Security Policy, `frame-ancestors 'none'`,
  `X-Content-Type-Options`, `Referrer-Policy`, `Permissions-Policy`; API answers are `no-store`. HSTS is set by the
  reverse proxy in front (the shipped `Caddyfile` does).
- **Requests** are limited to 1 MiB; endpoints that call out of the instance have a rate limit per account.
- **Container:** a non-root user on an image without shell and package manager, a read-only file system, all
  capabilities dropped. The database has no published port in the default Compose file.
- **Supply chain:** lock files for NuGet and npm, a licence allow-list in the build, automatic dependency updates,
  an SBOM and third-party notices in every image ([ADR 042](../adr/0042-sbom-and-third-party-notices.md)), and
  release images signed with provenance ([ADR 050](../adr/0050-releases-with-release-please-and-signed-images.md)).
