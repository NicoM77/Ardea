# Ardea

Ardea is Latin for heron: a bird that stands still for a long time and strikes only when it matters.

A self-hosted, open-source early-warning system for security vulnerabilities: it collects advisories and CVE records from trusted sources, merges them into one entry per vulnerability, and tells each organization which ones affect its projects, how urgent they are, and what to do.

## Status: phase 3 (findings and remediations)

The master plan is approved and implementation is under way. The tasks of phases 0 to 2 are done: an instance fetches and
consolidates vulnerabilities from five sources and keeps organisations, projects and their technology profile,
also read from GitHub repositories. Findings and remediations (phase 3) are in progress; there is no release
yet. Progress follows chapter 14 of the plan (see the roadmap below).

## Roadmap

The roadmap follows chapter 14 of the master plan: nine phases up to release 1.0, each ending with
something that runs and can be checked. Task numbers and dependencies are the plan's; the plan holds
the full description and the acceptance criteria of each phase. Estimates are for one experienced
person with AI assistance and are not a commitment.

Status: ✅ done · 🔄 in progress · ⬜ open

### Phase 0 · Foundation (5–6 weeks)

A startable instance with sign-in, job system, build chain and a first administration; technical risks resolved.

The phase 0 acceptance (healthy within 3 minutes of loading the images, then sign-in over HTTPS with setup token, password and TOTP) is checked by the end-to-end test in [`tests/e2e/`](tests/e2e/) (`tests/e2e/run.sh <image>`); CI runs it on `main` for now, pull requests are verified locally until task 7.13.

| No. | Task | Depends on | Status |
|---|---|---|---|
| 0.1 | Repository per chapter 13: solution, module skeletons, central package management, analyzers | | ✅ |
| 0.2 | Build chain: build, tests, lint, licence check, image for two architectures by cross-compiling; timing | 0.1 | ✅ |
| 0.3 | Host program with `api`, `worker`, `migrate`, `healthcheck`, `init-env`, `doctor`; Dockerfile; Compose with TLS proxy | 0.1 | ✅ |
| 0.4 | Job system per chapter 3.4; tests with forced aborts | 0.3 | ✅ |
| 0.5 | Tenant context and unit of work across modules; audit; settings; secret encryption; telemetry | 0.3 | ✅ |
| 0.6 | **Spike:** row-level security with EF Core, Npgsql, connection pool and shared transaction; ADR | 0.5 | ✅ [ADR 029](docs/adr/0029-row-level-security-as-hardening.md) |
| 0.7 | **Spike:** target image with time zones and culture; health check without shell; dump before migration; work volume for large archives | 0.3 | ✅ [ADR 028](docs/adr/0028-runtime-image-and-pre-migration-dump.md) |
| 0.8 | Sign-in: setup token, registration, server-side sessions with list and revoke, TOTP, password rules, CSRF protection, rate limiting | 0.5 | ✅ |
| 0.9 | Basic e-mail for confirmation, password reset and invitations; behaviour without SMTP | 0.3 | ✅ |
| 0.10 | Web: workspace with app and `ui` library; tokens from the mockups; header band, navigation, sign-in pages; i18n scaffold with English; component pages with visual comparison | 0.1 | ✅ |
| 0.11 | OpenAPI document at build time, check in the build, generated client | 0.8, 0.10 | ✅ |
| 0.12 | Architecture tests for module boundaries | 0.1 | ✅ |
| 0.13 | First administration: instance settings (registration, SMTP with test mail), abandoned jobs with retry, installed version | 0.4, 0.8, 0.10 | ✅ |

### Phase 1 · From source to consolidated entry (7–10 weeks)

Consolidated entries from five sources, including OSV for packages, with merging; searchable.

| No. | Task | Depends on | Status |
|---|---|---|---|
| 1.1 | Catalog module: product keys, CPE reader, version schemes (SemVer, NuGet, PEP 440, Maven, RPM, numeric), catalogue format | 0.1 | ✅ [ADR 030](docs/adr/0030-catalogue-format-and-master-data.md) |
| 1.2 | Provider SDK with checkpoints and signal interface; sync control: due time, cursor, back-off, health, run log | 0.4 | ✅ |
| 1.3 | HTTP foundation for adapters: timeout, retry, rate limiting, conditional requests, size limits | 1.2 | ✅ |
| 1.4 | CVE Program adapter: deltas, initial load and long gaps via the full archive, single fetch | 1.3 | ✅ |
| 1.5 | Raw record store with revisions; normalisation to observations; quarantine | 1.4, 1.1 | ✅ |
| 1.6 | Merge by CVE ID with fixed precedence rules and per-field provenance | 1.5 | ✅ |
| 1.7 | Change detection with both fingerprints; change history; follow-up job on material change | 1.6 | ✅ |
| 1.8 | Signals: KEV and EPSS in `entry_signal`, class change with threshold | 1.6 | ✅ |
| 1.9 | NCSC adapter with two readers: Cyber Security Hub API and BACS website warnings; `degraded` on suspicious fetches | 1.6 | ✅ |
| 1.10 | Entries API; «Entries» and «Sources» pages with quarantine list and per-source metrics; sources page mockup | 1.7, 0.11 | ✅ |
| 1.11 | Commands to re-normalise and recompute; completeness check against the change log | 1.7 | ✅ |
| 1.12 | OSV adapter for npm, NuGet, Packagist, PyPI, Maven and Go: `modified_id.csv`, initial load from the ecosystem archive; terms checked first | 1.3, 1.5 | ✅ [ADR 031](docs/adr/0031-osv-source.md) |
| 1.13 | Merging entries via a shared identifier that appears later, with redirect and review list for two CVE IDs | 1.6, 1.12 | ✅ |

Acceptance: [observed and tested](docs/acceptance/phase-1.md), first load in 51 minutes on 2 vCPU and 4 GB.

### Phase 2 · Organisations, projects and profile (7–9 weeks)

Organisations with projects and technology profile; profile from GitHub repositories; setup wizard.

| No. | Task | Depends on | Status |
|---|---|---|---|
| 2.1 | Organisations, memberships, roles, invitations, MFA requirement per organisation; «Team» page | 0.8, 0.9 | ✅ |
| 2.2 | Tenant isolation per the result of 0.6; member directory; database functions for system operations; cross-tenant test for all endpoints | 2.1 | ✅ |
| 2.3 | Projects and components with version, origin, exposure, criticality, mitigation; profile replacement as an ID-preserving reconcile | 2.1 | ✅ |
| 2.4 | Fill the catalogue: about 300 products with aliases, CPE mapping, related products; search | 1.1 | ✅ [ADR 030](docs/adr/0030-catalogue-format-and-master-data.md) |
| 2.5 | Search index `component_key` with match strength | 2.3, 2.4 | ✅ |
| 2.6 | Mockups for the setup wizard and the project profile; owner reviews afterwards | | ✅ |
| 2.7 | Setup wizard (repository, operations, technologies); project pages with links, packages and suggestions; organisation settings with access and audit log | 2.3, 2.4, 2.6, 2.13 | ✅ |
| 2.8 | Coverage hints: missing versions, platforms without components, distro packages, packages without lock file, ecosystems not yet checkable | 2.5 | ✅ |
| 2.9 | **Spike:** asynchronous SBOM export with a public and a private repository and a fine-grained token; ADR | | ✅ [ADR 032](docs/adr/0032-github-sbom-export.md) |
| 2.10 | GitHub access with encrypted token, check, expiry, list of readable repositories; GitHub Enterprise Server hosts in administration | 2.3, 2.9 | ✅ |
| 2.11 | Connection via `IRepositoryHost`: branch head with ETag, SBOM request, file tree, files with limits; readers for SPDX, `packages.lock.json`, `package-lock.json` | 2.10 | ✅ |
| 2.12 | Detection rules for Dockerfile, Compose, `global.json`, project files, `engines` and `.nvmrc`, `composer.json`, `go.mod`; image names in the catalogue; suggestions with evidence | 2.4, 2.11 | ✅ |
| 2.13 | `IndexRepository` job with due time and system operation; reconcile per 16.5 holding back much smaller states; `repository.unavailable`; audit with commit as origin | 2.5, 2.11, 2.12 | ✅ |

Acceptance: [observed and tested](docs/acceptance/phase-2.md); open: the timed wizard run with a test person and reading Ardea's own repository with a token.

### Phase 3 · Findings, actions and their UI (6–8 weeks)

Findings, actions and alerts with level and reasoning; overview, list, detail.

| No. | Task | Depends on | Status |
|---|---|---|---|
| 3.1 | Mockups: calm overview, action with several findings, version with AI assessment; owner reviews afterwards | | ✅ |
| 3.2 | Candidate search forward and backward, including OSV package coordinates; match strength; weak matches as level 1 | 1.7, 1.12, 2.5 | ✅ |
| 3.3 | Version check with three outcomes including the records' default status; distro packages as unknown | 3.2, 1.1 | ✅ |
| 3.4 | Rule set version 1 as a file; factors, upper and lower bounds, deadlines | 3.3 | ✅ |
| 3.5 | Findings, actions, alerts, history; events from 8.2 as history lines; closing by raising the version in the profile; initial load without single events | 3.4 | ✅ |
| 3.6 | Answers from templates without a language model | 3.5 | ✅ |
| 3.7 | Reference set with at least 200 labelled pairs; spike week as test data; evaluation run in the build; weight calibration | 3.4 | ✅ |
| 3.8 | Overview with warnings without identifier and calm state | 3.5, 3.1 | ✅ |
| 3.9 | Actions list with filters for priority and project, search, open and closed | 3.5, 3.1 | ✅ |
| 3.10 | Action and vulnerability detail; status, assignment, deadline, note, revert | 3.5, 3.1 | ✅ |
| 3.11 | Live updates via Server-Sent Events; command to recompute levels | 3.8 | ✅ |

Acceptance: [observed and tested](docs/acceptance/phase-3.md); open: the owner's review of the reference set's labels and of the mockups, and the decision on bundling transitive packages (34 open actions from the three repositories against 6 for the spike week).

### Phase 4 · AI assessment (3–4 weeks)

AI assessment as an optional step; a catalogue that grows by itself.

| No. | Task | Depends on | Status |
|---|---|---|---|
| 4.1 | Integration via Microsoft.Extensions.AI; named providers marked local or not; connection test; settings in administration | 3.5 | ✅ |
| 4.2 | Prompt, output schema, output validation, one retry, fallback to rules | 4.1 | ✅ [ADR 037](docs/adr/0037-assessment-by-language-model.md) |
| 4.3 | Trigger as its own job by threshold and quota per action; on demand in the UI | 4.2 | ✅ |
| 4.4 | Data-minimal input; data policy per organisation | 4.2 | ✅ |
| 4.5 | Model verdict as a factor only for unknown versions; «not affected» as a suggestion to confirm | 4.2, 3.4 | ✅ |
| 4.6 | Limits: concurrency, quotas, timeout; metrics | 4.1 | ✅ |
| 4.7 | «Correct assessment» with re-evaluation | 4.2, 3.10 | ✅ [ADR 039](docs/adr/0039-review-of-an-assessment.md) |
| 4.8 | Integration tests with recorded model answers, including invalid and manipulated ones; local model recommendation in the guide | 4.2 | ✅ |
| 4.9 | Catalogue learning (plan 6.9): collect unknown products, rule-based mapping without a model, `GrowCatalog` job with the model, validation, auto-apply by confidence, review list, recompute | 4.2, 1.11, 2.4 | ✅ |
| 4.10 | Withdraw learned entries; note on the match; suggestions for organisations' free entries; export in the catalogue file format; metrics in administration | 4.9, 3.10 | ✅ |

Acceptance: [tested](docs/acceptance/phase-4.md) against recorded model answers; open: the observation with a local model at 50 unknown keys, and the owner's review of the AI mockups.

### Phase 5 · Notifications (3–4 weeks)

Notifications over four channels.

| No. | Task | Depends on | Status |
|---|---|---|---|
| 5.1 | Rule evaluation for the events from 3.5; distribution of warnings without identifier; recipient resolution; confirmation and opt-out for external addresses | 3.5 | ✅ [ADR 038](docs/adr/0038-notifications.md) |
| 5.2 | Notification state, uniqueness per recipient, channel and event, bundling, digest queue | 5.1 | ✅ [ADR 038](docs/adr/0038-notifications.md) |
| 5.3 | In-app channel with live delivery | 5.2, 3.11 | ✅ [ADR 038](docs/adr/0038-notifications.md) |
| 5.4 | E-mail channel with templates for immediate and digest messages | 5.2, 0.9 | ✅ [ADR 038](docs/adr/0038-notifications.md) |
| 5.5 | Web Push channel: key pair, subscriptions, service worker, manifest, iOS install guide | 5.2 | ✅ [ADR 041](docs/adr/0041-web-push.md) |
| 5.6 | Webhook channel with signature and protection against internal targets | 5.2 | ✅ [ADR 038](docs/adr/0038-notifications.md) |
| 5.7 | Default rules; scheduled digests; personal mute | 5.2, 5.4 | ✅ [ADR 038](docs/adr/0038-notifications.md) |
| 5.8 | Mockup and pages for rules, targets, delivery log, personal settings; owner reviews afterwards | 5.1 | ✅ [mockups](docs/design/mockups/notification-rules.html) |

Acceptance: [tested](docs/acceptance/phase-5.md); open: the manual iOS check on an installed web app, a test with a real push service, and the owner's review of the notification mockups.

### Phase 5a · Stack and notices (4–6 weeks)

The profile gets two depths (chapter 17 of the plan): a coarse stack for the organisation without versions, and exact projects with a repository. Entries that touch the stack become notices («might affect you») answered with «affects us» or «doesn't affect us». «Affects us» creates a reminder. Actions become reminders: no assignment, no deadline, no escalation; open reminders remind by level.

| No. | Task | Depends on | Status |
|---|---|---|---|
| 5a.1 | **Spike:** notices per week for typical stacks with the spike week's data; set thresholds; ADR | 3.7 | ⬜ |
| 5a.2 | Data model: `project.kind`, `version_precision`, `notice`; remove assignment, deadline and «in review»; migrate open actions without a version to notices | 5a.1 | ⬜ |
| 5a.3 | Notices from the stack with a threshold per entry; «affects us» creates a reminder; reappears on active exploitation or KEV | 5a.2 | ⬜ |
| 5a.4 | Wizard: stack step before projects; projects and repository optional | 5a.2 | ⬜ |
| 5a.5 | Notices page; shortened reminder detail; overview with new notices | 5a.3 | ⬜ |
| 5a.6 | Reminders by level; event `notice.raised`; escalation dropped | 5a.3, 5.2 | ⬜ |

Acceptance: a stack of ten entries produces at most the number of notices set in 5a.1 in the spike week; «doesn't affect us» does not come back; no endpoint or page knows assignment or deadline.

### Phase 6 · Hardening (3–4 weeks)

A hardened, reviewed and documented instance.

| No. | Task | Status |
|---|---|---|
| 6.1 | Security pass along the threat table in 9.7; headers, rate limiting, tests against internal targets and injected content | ✅ [threat review](docs/security/threat-review.md) |
| 6.2 | Independent security review by someone outside development: sign-in, tenant isolation, outbound connections | ⬜ |
| 6.3 | Manual accessibility pass of the core flows with keyboard and screen reader | ⬜ |
| 6.4 | Indexes checked against query plans; memory and response times observed on the project's own instance | ⬜ |
| 6.5 | Fast integration test: restore a small pre-release dump, migrate to 1.0, restore again | ✅ |
| 6.6 | Maintenance commands: recover access, load full dataset, export and delete an organisation; self-warnings; operations metrics | ✅ |
| 6.7 | Documentation: install, configure, operate, update, «Add a source»; `SECURITY.md` | ⬜ |
| 6.8 | SBOM and licence file in the build; the project's own instance, fed from its own repository | ⬜ |

Rows stay ⬜ where a part is open: **6.4** the indexes are checked (plan tests for every list and search query of phases 3 to 5, three new indexes), memory and response times on the project's own instance are not observed yet; **6.7** the guides are complete, `SECURITY.md` belongs to task 6.1; **6.8** SBOM and licence file are in the build and the image ([ADR 042](docs/adr/0042-sbom-and-third-party-notices.md)), the project's own instance needs the owner's infrastructure.

### Phase 7 · Clean-up and release 1.0 (4–5 weeks)

A tidy repository under the name Ardea, release 1.0 built without manual steps, a product website with the documentation.

| No. | Task | Depends on | Status |
|---|---|---|---|
| 7.1 | Public face: repository renamed from `CSH-Alert` to `Ardea` (✅ done 19.11.2026, `NicoM77/Ardea`) before the first image, so `ghcr.io/<owner>/ardea` and the website address are right from the start; trademark search (E1) still due before the first public release; description, topics, social preview from the logo; issue and PR templates; branch protection with required checks (needs 7.13); Dependabot for NuGet, npm, GitHub Actions and base images; CodeQL for C# and TypeScript as a job of its own within the time budget | 7.13 | 🔄 |
| 7.2 | Versioning with release-please: Conventional Commits keep a release PR with version number and `CHANGELOG.md` up to date; merging it sets the tag and creates the GitHub release with notes; a PR check lets only Conventional Commits through; version in image, `doctor` and administration; `0.x` marked as pre-releases | 7.1 | ⬜ |
| 7.3 | Release pipeline in the same workflow as 7.2 (a tag set with `GITHUB_TOKEN` starts no other workflow): `amd64` and `arm64` images into `ghcr.io` with the tags from chapter 12; keyless cosign signature via the workflow's OIDC identity and build provenance; SBOM, `compose.yaml` and sample configuration attached to the release; command to verify signature and provenance in the install guide | 6.8, 7.2 | ⬜ |
| 7.4 | Repository clean-up: inventory of every file outside `src`, `tests`, `web`, `deploy` and `build`; delete planning and design leftovers (`.impeccable/`, `prototypes/`, mockups, review images, research) with a per-file decision list in the PR; delete orphaned scripts, configuration and fixtures; scan the Git history for secrets and large files | 6.7 | ⬜ |
| 7.5 | Code clean-up: remove comments, keeping only those something depends on: XML docs the OpenAPI document takes its descriptions from, justifications on suppressed analyzer and lint warnings, tool directives; a build check keeps new ones out; remove unused code, packages, translation keys, settings and test helpers (analyzers, `knip` for the web app); resolve every `TODO` or turn it into an issue | 6.1 | ⬜ |
| 7.6 | `seed-demo` command: loads the sample data (organisation, three projects, entries, findings, actions, notifications) into an empty instance without GitHub token, initial load or source fetches; refuses a database that holds data; «Try it» guide | 6.6 | ✅ [Try it](docs/operations/try-it.md) |
| 7.7 | Screenshots generated by Playwright from a `seed-demo` instance for README and website (overview, action, vulnerability, wizard, phone width), so they stay current with the UI | 7.5, 7.6 | ⬜ |
| 7.8 | English README: logo in light and dark; purpose in two sentences; screenshots; features; quick start in three commands and «Try it» with `seed-demo`; requirements; sources with their terms of use; links to website and docs; contributing, security, licence; the roadmap shrinks to what comes after 1.0 | 7.4, 7.7 | ⬜ |
| 7.9 | Website on GitHub Pages: product landing page in the design system of `DESIGN.md` with logo, purpose, screenshots, features, quick start and a link to the latest release; below it the documentation (install, configure, operate, update, «Add a source», architecture, API from the OpenAPI document, ADRs), read from the Markdown files in `docs/` as the single source; static generator (recommended: Astro with Starlight) with the same licence check as the web app; built and published on every release and on changes to `docs/` on main; link and accessibility checks in the build | 7.1, 7.7, 7.8 | ⬜ |
| 7.10 | Measure coverage, without a quota; close gaps in sign-in, tenant isolation, version checks, rule set and notifications with fast tests within the time budget | 7.5 | 🔄 |
| 7.11 | Acceptance of all phases: the criteria of phases 0 to 5 and chapter 1.6 on a fresh install from the release images (`amd64`, `arm64`); update from the last `0.x` to 1.0; the two manual passes; result in `docs/acceptance/release-1.0.md` | 7.3, 7.10 | ⬜ |
| 7.12 | Delete the master plan: first move what still applies into the English docs (architecture and module boundaries, data model in brief, rule set and levels, security model, the chapter 13 rules into `CONTRIBUTING.md`, the roadmap after 1.0); then delete `docs/masterplan/` and fix every link to it in ADRs, acceptance records, README, `CONTRIBUTING.md` and code | 7.9, 7.11 | ⬜ |
| 7.13 | CI on pull requests again: undo the temporary restriction of 19.11.2026 (Actions minutes used up, so CI runs only on main and pull requests are verified locally); the workflow runs on every pull request again, cancelling superseded runs and skipping documentation-only changes; visual comparison and `arm64` image automatic on main again; drop the note in `ci.yml` | | ⬜ |
| 7.14 | Branch clean-up: delete every branch merged into main, on GitHub and locally; review the rest one by one (closed PR without merge, stale work) and delete it or continue it as an open PR, with a per-branch decision list; turn on «Automatically delete head branches» so merged branches go away by themselves | 7.1 | ⬜ |

Acceptance (planned): every pull request runs the pipeline again and branch protection requires it green (7.13); merging the 1.0.0 release PR produces tag, `CHANGELOG.md`, signed images for both architectures and a GitHub release with notes, SBOM and Compose file without manual steps, within 25 minutes; signature and provenance verify with the documented command; a commit that is not a Conventional Commit fails its PR; `seed-demo` yields an instance with data in under 2 minutes and aborts on a database with data; someone new to the project installs Ardea from the README or website alone in under 15 minutes; the website republishes on a release with no broken link and no accessibility finding; all criteria of phases 0 to 5 and chapter 1.6 hold on both architectures; build and lint run without warnings, the comment check finds nothing outside the exceptions and the OpenAPI document is unchanged; GitHub holds no branch besides main except those of open PRs, and a merged PR deletes its branch (7.14); afterwards no file links to `docs/masterplan/` and every file belongs to the product, its build, tests, documentation or website.

**Total: 42–56 weeks** (phase 5a added 19.11.2026).

### After 1.0

In this order (master plan, chapter 14):

1. **First source wave:** generic CSAF reader, OSV for distributions and further ecosystems (RubyGems, crates.io, GitHub Actions), ENISA EUVD and the other sources of wave 1 in chapter 5.6.
2. **More ways into the profile:** GitHub App with webhooks, SBOM upload (CycloneDX, SPDX), more lock files; then GitLab and Azure DevOps.
3. **German** in the UI and in messages.
4. **Escalation** in steps; templates for Teams and Slack.
5. **Passkeys and OIDC sign-in;** API tokens.
6. **Products from free text** via a language model; corrections as context; weights per organisation.
7. **Capacitor app,** once the push relay is decided.
8. **30 to 50 sources;** reports and export; per-project permissions; master key rotation; pause on suspicious mass changes from a source.

## Try it

Look at Ardea with sample data: an organisation, three projects, real CVE records of the week of
11 to 17 November 2026 and the actions and notifications the pipeline makes of them. No GitHub token, no
initial load, no access to any source. With Docker Compose and an image (built with
`docker build -f deploy/Dockerfile -t ardea:dev .` until the first release):

```bash
docker compose up -d --wait api        # database, migration, API; the worker comes after the data
docker compose run --rm -e ARDEA_DB_OWNER_PASSWORD="$(sed -n 's/^ARDEA_DB_OWNER_PASSWORD=//p' .env)" api seed-demo
docker compose up -d --wait            # the worker; with the sources switched off it fetches nothing
```

`seed-demo` prints the demo owner's e-mail address and a generated password once; it refuses a database that
already holds data or that a worker has used. The demo is for local evaluation only, never for an instance others
can reach or that will become production. The full guide, with the first steps (`compose.yaml` and `init-env`) and what the demo
contains, is [`docs/operations/try-it.md`](docs/operations/try-it.md).

## Development

Requirements: .NET SDK 10.0.4xx (pinned in `global.json`), Docker for integration tests, Node 24 for the web app.

```bash
dotnet build Ardea.slnx
dotnet test --solution Ardea.slnx
dotnet format Ardea.slnx --verify-no-changes
build/check-licenses.sh            # NuGet licences against the allow-list
python3 build/check-banned-packages.py
```

Web app (`web/`, Angular 22, Node 24): `npm ci`, then `npm start` for the dev server (proxies
`/api` to `http://localhost:5080`), `npm test`, `npm run lint`, `npm run build`. The component
library pages are at `/ui` in development builds. Details in [`web/README.md`](web/README.md).

Run an instance locally from source:

```bash
docker compose -f deploy/compose.dev.yaml up -d      # PostgreSQL 18 and a mail catcher
set -a; . deploy/dev.env; set +a
dotnet run --project src/Hosts/Ardea.Host -- migrate
dotnet run --project src/Hosts/Ardea.Host -- api     # http://localhost:5080
dotnet run --project src/Hosts/Ardea.Host -- worker
```

Or build the image and start a complete instance the way an operator would (`deploy/compose.yaml`):

```bash
docker build -f deploy/Dockerfile -t ardea:dev .
build/smoke-test.sh ardea:dev
```

The repository layout and the rules for code, dependencies and tests are in chapter 13 of the
master plan and in [`CONTRIBUTING.md`](CONTRIBUTING.md).

| What | Where |
|---|---|
| Master plan (German) | [`docs/masterplan/`](docs/masterplan/README.md) |
| Decision register | [`docs/adr/`](docs/adr/README.md) |
| Product record and design system | [`PRODUCT.md`](PRODUCT.md), [`DESIGN.md`](DESIGN.md) |
| UI mockups (static HTML, open `overview.html` in a browser) | [`docs/design/mockups/`](docs/design/mockups/) |
| Ecosystem research with sources, 18 Nov 2026 | [`docs/research/`](docs/research/) |
| Throwaway data spike | [`prototypes/`](prototypes/README.md) |
| Contribution rules | [`CONTRIBUTING.md`](CONTRIBUTING.md) |
| Running an instance: installation, configuration, operation, update, adding a source | [`docs/operations/`](docs/operations/README.md) |
| Reporting a security issue | [`SECURITY.md`](SECURITY.md) |

## Planned stack

ASP.NET Core on .NET 10, Angular 22, PostgreSQL 18, one container image with three roles (API, worker, migration), Docker Compose. AI assistance is optional and provider-neutral, including locally hosted models.

## Licence

The application is licensed under the [GNU Affero General Public License v3.0](LICENSE). The provider SDK
(`src/Sources/Ardea.Intel.Sources.Abstractions`) is licensed under the
[Apache License 2.0](src/Sources/Ardea.Intel.Sources.Abstractions/LICENSE), so that third parties can write
source adapters under a licence of their choice (decision E2, master plan 13.9).
