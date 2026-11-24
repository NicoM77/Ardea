# Release acceptance: all phases on the release images

Master plan chapter 14, task 7.11: the criteria of phases 0 to 5 (with 5a), of phases 6 and 7 and of chapter 1.6, checked
on a fresh install from the release images for `amd64` and `arm64`; the update from the last `0.x` to 1.0; the two manual
passes of chapter 13.6. This record is the cross-phase acceptance. Where a phase record (`phase-1.md` to `phase-5a.md`)
already maps a criterion to a test or an observation, that evidence is reused and not repeated.

**There is no 1.0 yet.** This run was made on **release 0.1.1** (pre-release, 20.11.2026) and is the dress rehearsal
for 1.0. Everything that needs the 1.0 images, or a person, is listed under [Open for 1.0](#open-for-10). The task is done
when the same run on the 1.0 images leaves that list empty.

## What was accepted

| | |
|---|---|
| Release | [v0.1.1](https://github.com/NicoM77/Ardea/releases/tag/v0.1.1), published 20.11.2026 14:36:57 UTC as a pre-release, with `compose.yaml`, `compose.caddy.yaml`, `Caddyfile`, two CycloneDX SBOMs and `THIRD-PARTY-NOTICES.md` attached |
| Image | `ghcr.io/nicom77/ardea:0.1.1`, index `sha256:6d416c35a2f440e93cdeaa86636d7b3b2dc39b5f6e78d120d6054cf965fcf784`; the tags `0` and `0.1` resolve to the same index |
| `linux/amd64` | `sha256:933ed7335263229969a7a64422a2e7afca9721f8137dc22a2aed451d7004229b` |
| `linux/arm64` | `sha256:9fd363212d1b3f7e41e335af3e1c3e51c6197723e9f37309d2b8f6c61549dd02` |
| `v0.1.0` | Tag only, no images (its build hit the cache race fixed in #156; the draft release was deleted) |

Two sources of evidence on the release images:

1. **The acceptance workflow** (`.github/workflows/acceptance.yml`), run
   [44755319850](https://github.com/NicoM77/Ardea/actions/runs/44755319850) for 0.1.1, all three jobs green. This is the
   only evidence for `arm64` (see [arm64](#arm64)).
2. **A walk on an `amd64` host** (8 cores, 16 GB, Docker 29.8, Compose 5.5), 20.11.2026 14:41 to 15:00 UTC: a fresh
   install exactly as the README and `docs/operations/installation.md` say, a second fresh install with `seed-demo` for
   the core pages, and an update from an older build to 0.1.1. All instances were removed with their volumes afterwards.

## The walk on amd64

### Fresh install with live sources

Files from the README (`compose.yaml`, `compose.caddy.yaml`, `Caddyfile`), `init-env` from `ghcr.io/nicom77/ardea:0`, the
bundled Caddy with `ARDEA_DOMAIN=localhost` and moved ports (HTTPS 8551, HTTP 8191, API 18111), all sources on, no
language model, no SMTP.

| Step | Time (UTC) | |
|---|---|---|
| `mkdir`, download of the three files | 14:41:24 | the image was not on the host; the first `docker run … init-env` pulled it |
| `init-env` | 14:41:30 | `.env` and `.env.app` written, mode 0600 |
| `docker compose up -d --wait` (after `pull` of `postgres:18` and `caddy:2`) | 14:41:39 | |
| All services healthy | 14:41:57 | **18 s**; 48 migrations applied, no dump (empty database) |
| `ardea version` | | `0.1.1` |
| `ardea doctor` | | every line `ok`: version 0.1.1, configuration, time zones, ICU, database PostgreSQL 18.7, schema up to date, no SMTP, no AI provider, and the hosts of all six sources reachable |
| Setup token, first user, TOTP on, sign-out, sign-in with password and TOTP | 14:43:26 | the release's own flow `tests/e2e/first-sign-in.spec.ts` (from the tag `v0.1.1`) against this instance: passed in 38 s, with axe on 18 pages |
| First entries in the system | 14:44:53 | **3 min 14 s** after `up` (143 entries; the waiting loop polled every 10 s) |
| State at 14:55 (about 13 minutes of first load) | 14:55:05 | 39 345 entries; CVE Program 94 510 records, OSV 23 906, KEV and EPSS healthy; **NCSC Cyber Security Hub and BACS failed** (below); 2 jobs dead, not inspected before the teardown |

From an empty folder to signed in with TOTP took **2 minutes**. That is a proxy for the criterion «a person who does
not know the project installs Ardea from the README in under 15 minutes»: the commands were known, and the sign-in was
done by the Playwright flow, not by hand. It shows that nothing in the documented path waits or fails; it does not
replace the person.

The NCSC Cyber Security Hub and the BACS warnings failed on their first run with «answered without a content type instead
of JSON; the interface may have changed», exactly as on 19.11.2026 (`phase-1.md`, open point), while `doctor` reached
both hosts. From this host, `https://security-hub.ncsc.admin.ch/` answers `200 text/html`. The other four sources ran.

`seed-demo` on this instance (worker already started) **refused** with exit code 1: «The database is not empty
(1 worker heartbeat(s), 3 job(s)) … changed nothing.»

### Fresh install with `seed-demo`, walked in the browser

A second instance (`ardea-acc711-demo`, HTTPS 8552) as `docs/operations/try-it.md` says, but behind the bundled Caddy:
`up -d --wait api`, then `seed-demo`, then the worker.

- `seed-demo`: **15.6 s** wall time including the container start («Demo instance ready in 8 s: 1 organisation, 3
  projects, 16 entries, 11 relevant findings, 5 open and 2 done actions, 42 notifications in the app»). No GitHub token,
  and the sources stay switched off.
- `seed-demo` a second time: **refused** with exit code 1 («3 account(s), 1 organisation(s), 4 project(s), 16 source
  record(s), 16 security entry/entries, 1 worker heartbeat(s), 95 job(s) … changed nothing»); counts before and after
  identical.

A Playwright walk in `mcr.microsoft.com/playwright:v1.56.1-noble` (Chromium, 1440 × 900, then 390 × 844), signed in as
the demo owner, ran axe (WCAG 2.0, 2.1 and 2.2 A and AA) on every page and recorded console errors:

| Page | What was checked | axe |
|---|---|---|
| Sign-in | sign-in with password | 0 |
| Overview | «5 new notices to answer. 1 critical reminder is open. 1 actively exploited, 1 with a fix.»; the open reminders with the three answers; notices card | 0 |
| Reminders | list with 5 open reminders | 0 |
| Reminder «Update PHP to 8.4.27» | «What happened», «Are we affected?», «What to do» shown; no «Owner» or «Due» field; «Mark as remediated» and Undo against the real API | 0 |
| Vulnerability (alert) and its entry `CVE-2026-109618` | detail pages | 0 |
| Vulnerabilities | list of entries | 0 |
| Notices | inbox with the three answers per card; one answered «Doesn't affect us» («It comes back once if it gets exploited.», with Undo) | 0 |
| Notifications | in-app inbox | 0 |
| Stack, Projects, Settings, Your notifications, Notification rules, Team | pages load with data; the stack is not in the projects list | 0 |
| Administration, Sources | «Installed version 0.1.1»; six sources, all paused | 0 |
| Reminder, Overview, Notices at 390 × 844 | the three answers end at 410, 519 and 672 px, inside the 844 px screen | 0 |

21 page views, **no axe finding, no console error**. The phone view showed one bug: the reminder's action bar is not
pinned to the bottom of the screen (bug 1 below).

### Update proxy

There is no 1.0 and no earlier `0.x` with images, so the update from the last `0.x` to 1.0 cannot be run. Two proxies,
labelled as such:

1. **Older build → 0.1.1 release image, with data.** An image built from `main` at `09408f4` (20.11.2026, one migration
   behind 0.1.1, reports `0.0.0-pre.09408f4`), installed fresh with `seed-demo` and the worker, then updated as
   `docs/operations/update.md` says (`ARDEA_IMAGE=ghcr.io/nicom77/ardea:0.1.1`, `pull`, `up -d`):
   - healthy after 13 s; `migrate`: «Dump before migration written on the database server:
     /var/lib/postgresql/backup/ardea-20261120T145631Z.dump», «Schema notify: applied 1 migration(s)»;
   - `doctor` all `ok` with version `0.1.1`; users, organisations, projects, components, entries, findings, reminders,
     notices and notifications counted the same before and after;
   - the browser walk above ran green on the updated instance (sign-in with the password `seed-demo` printed before the
     update, axe 0 on all pages, no console error);
   - **going back** as `update.md` says (stop API and worker, drop and create the database, `pg_restore` the dump, pin
     the previous image): restore exit 0, `migrate` applied nothing, the instance came up on `0.0.0-pre.09408f4` with the
     same counts as before the update.
2. **0.1.1 → image built from current `main`** (`7ddbe8b`, same code as 0.1.1 plus a workflow) on the seeded instance:
   healthy after 18 s, «0 migration(s) applied, dump: none», `doctor` all `ok` with the new version string, counts
   unchanged. This shows only that an image swap on existing volumes keeps the data.

Neither is the update from the last `0.x` to 1.0. Until 1.0 the path is covered by `PreReleaseRestoreTests` (task 6.5:
a pre-release dump restored, migrated to this build, restored from the dump `migrate` wrote, migrated again) on every
build, and by `docs/operations/update.md`.

## arm64

This host cannot run `arm64` images. The evidence is the acceptance workflow, run
[44755319850](https://github.com/NicoM77/Ardea/actions/runs/44755319850) on 20.11.2026:

| Job | Runner | Result |
|---|---|---|
| Signature and attestations | `ubuntu-latest` | success: `cosign verify` of the index and of each architecture's digest (`amd64 sha256:933ed733…`, `arm64 sha256:9fd36321…`) against the identity of `release.yml` on `main`; `gh attestation verify` of the build provenance and the CycloneDX SBOM of the index and of both digests, GitHub-hosted runners only |
| Fresh install (arm64) | `ubuntu-24.04-arm`, native | success: the pulled image is `arm64` and reports `0.1.1`; `tests/e2e/run.sh` from a fresh `init-env` behind Caddy: **healthy after 16 s**, the flow `first-sign-in.spec.ts` passed (41 s): setup token, password, TOTP, administration with the installed version, sources, vulnerabilities, a new organisation through the wizard (stack, project, operation, technologies, coverage), team, invitation accepted, axe on 18 pages |
| Fresh install (amd64) | `ubuntu-24.04`, native | success: the same, healthy after 13 s, the flow passed (1.0 min) |

Not checked on `arm64`: live sources (first entries, first load), `seed-demo` and the pages behind it, `doctor`, and
the update path. The flow on `arm64` covers the start, sign-in and the setup pages; the pages with data were walked on
`amd64` only.

Verifying locally: `cosign verify` with cosign 3.0.2 (its official container) passed here for the index and both
digests. `gh attestation verify` could not run on this host: its `gh` 2.45 has no `attestation` command. The install
guide asks for «the GitHub CLI» without a version; the workflow's verification stands for the attestations.

## Phase 0 · Foundation

| Criterion | Evidence | Result |
|---|---|---|
| With the sample configuration, an instance where one signs in over HTTPS with setup token, password and TOTP is up in under 3 minutes after the images are loaded | Release images: healthy after 18 s (amd64, this host), 13 s (amd64) and 16 s (arm64) in the workflow; the sign-in flow passed on all three; `run.sh` fails above 180 s | **Met** on both architectures |
| 10 000 queued jobs are processed by two workers; a worker killed mid-work: each of its jobs runs again and its effect happens exactly once | `JobSystemTests.Ten_thousand_jobs_run_exactly_once_on_two_workers_even_when_one_is_killed`, `JobSystemTests.Jobs_of_a_killed_worker_run_again_and_their_effect_happens_once` | Met in tests only |
| A job and its business change are committed together or not at all (forced abort) | `JobSystemTests.Job_and_its_cause_are_committed_together_or_not_at_all` | Met in tests only |
| An ADR exists for spikes 0.6 and 0.7 | ADR 029 (row-level security), ADR 028 (runtime image and pre-migration dump); the dump was seen working in the update proxy | Met |
| A PR with an excluded library or a module-boundary violation fails the build | CI job «Dependencies», step «Excluded libraries» (`build/check-banned-packages.py`); `Ardea.Architecture.Tests` (`ModuleBoundaryTests`) in the job «Backend» | Met in tests only (no failing PR was made for this record) |
| The pipeline of a pull request runs in under 15 minutes | CI on pull requests of 20.11.2026: 6 to 8 minutes (for example run 44753684582, 8 min); CodeQL alongside, 4 to 6 minutes | Met |

## Phase 1 · From source to consolidated entry

All criteria are in `phase-1.md` with their tests and the observed first load of 19.11.2026 (51 minutes, 0 % quarantine).
On the release image this run observed only the start of the first load:

| Criterion | Evidence on 0.1.1 | Result |
|---|---|---|
| First load with 180 days look-back without manual work, under 60 minutes on the reference machine | Not timed to the end on 0.1.1 (13 minutes observed: 94 510 CVE records, 23 906 OSV records, 39 345 entries, no manual step). Measured on 19.11.2026 on the reference limits: 51 min (`phase-1.md`) | Met (`phase-1.md`); not repeated on the release |
| A new or changed CVE record is in the system within 90 minutes | By design, as `phase-1.md` | Met by design; not observable in this run |
| The same delta twice changes nothing; no missing id against a change log with gaps; one entry per CVE with provenance and the national mark; 48-hour outage and the archive path; EPSS touches only class changes; GHSA → CVE merge; version check in six ecosystems | Tests named in `phase-1.md` (green on the commit of 0.1.1) | Met in tests only |
| Quarantine share for CVE Program under 0.5 % | 0 of 94 170 on 19.11.2026; not counted on 0.1.1 | Met (`phase-1.md`) |
| NCSC advisories as a further source with the national mark | Recorded responses in tests; **the live NCSC and BACS fetches failed again** from this network on 0.1.1 | Met in tests only; live fetch **open** (carried from `phase-1.md`) |

## Phase 2 · Organisations, projects and profile

| Criterion | Evidence | Result |
|---|---|---|
| Cross-tenant test, catalogue search at 50 inputs, audit of profile changes, ids kept when a profile is replaced, one component changed per raised package version, shrunk SBOM held back, hostile repository read as data | Tests named in `phase-2.md` | Met in tests only |
| The wizard on the release: stack step, project, operation, technologies, coverage | `first-sign-in.spec.ts` on 0.1.1, both architectures and this host | Met |
| A test person enters the three example projects in the wizard in under 10 minutes, also with one from a repository | Needs a person | **Open** (carried from `phase-2.md`) |
| Ardea's own repository and two others read; counts observed | Two public repositories and `expressjs/express` observed (`phase-2.md`); Ardea's own repository needs the owner's token. The repository is public since 20.11.2026, so it can now be read without one | **Open** for Ardea's own repository |

## Phase 3 · Findings, reminders and the interface

| Criterion | Evidence | Result |
|---|---|---|
| Reference set thresholds; worked examples of 7.6; same input same level; spike data at most 8 open reminders; 40 hits as first fill; raised version closes findings; development-only at most level 2 | Tests named in `phase-3.md` | Met in tests only; the labels still await the owner's review (`phase-3.md`) |
| The three repositories give a recorded number of open reminders; the owner decides on bundling transitive packages | 34 recorded (`phase-3.md`) | **Open**: the owner's decision |
| With the sample data, all three answers above the action bar at 390 × 844 | On the release with `seed-demo`: the three answers end at 672 px of 844. The action bar itself is not on the screen (bug 1); the layout test `web/e2e/flows.spec.ts` «the three answers sit above the action bar» only checks that the bar starts below 700 px, which an off-screen bar also does | Met for the answers; see bug 1 |
| Without a language model, overview, reminder and alert show all three answers from structured data | On the release, without a provider (`doctor`: «no provider set»): overview, reminder, notices and alert walked with data | **Met** on the release (amd64) |
| Automatic accessibility check of the pages finds nothing | axe on 21 page views with data (amd64) and on the 18 pages of the release flow (both architectures); axe in every flow of `web/e2e/flows.spec.ts` | **Met** |

## Phase 4 · AI assessment

No language model was configured on the release instances. The criteria with a model are met against recorded answers
(`phase-4.md`).

| Criterion | Evidence | Result |
|---|---|---|
| Without a provider the system behaves exactly as after phase 3 | Every instance of this run had no provider; overview, reminders, notices and alerts answered from rules and templates | Met; on the release without a provider |
| 20 entries with embedded instructions; no components outside the hits and no project names in requests; «local only» never reaches a cloud provider; at most 40 calls for the spike week; learned entries change no curated entry; instructions in product names; mapping without a model | Tests named in `phase-4.md` | Met in tests only |
| Once with a local model at 50 unknown keys | Not run (`phase-4.md`) | **Open** |

## Phase 5 · Notifications (chapter 8.8)

| Criterion | Evidence | Result |
|---|---|---|
| Three times the same event gives one delivery per recipient and channel; two rules one message; rise notifies again; bundling within two minutes; 40 hits one message; warnings without id once per organisation; digests in the organisation's time zone; SMTP outage delays but does not lose; webhook signature; private targets refused; push encryption and a gone subscription | Tests named in `phase-5.md`, with fake clock and fake services | Met in tests only |
| In-app notifications on the release | `seed-demo` made 42 in-app notifications through the real pipeline; the inbox shows them (walk, amd64) | Met on the release (amd64) |
| Web Push on iOS on an installed web app; a real push service | Need a person with an iPhone and real push services | **Open** (also the second manual pass of 13.6) |
| Digests, retries and reminders over days on a real instance | Not observable in a one-off acceptance; the place is the project's own instance (6.8) | **Open** |

## Phase 5a · Stack and notices

| Criterion | Evidence | Result |
|---|---|---|
| A stack of ten entries gives at most 25 notices in the spike week | `NoticeSpike` (opt-in, ADR 047) and `ReferenceSetTests` (`phase-5a.md`) | Met in tests only |
| «Doesn't affect us» does not come back (except once on exploitation or KEV) | Tests in `phase-5a.md`; on the release a notice answered «Doesn't affect us» left the inbox and said «It comes back once if it gets exploited.» | Met; the return itself in tests only |
| No endpoint and no page knows assignment or deadline | `OpenApiContractTests.No_endpoint_knows_assignment_deadline_or_review_state`; on the release the reminder page has no «Owner» or «Due» field and the overview no deadlines | Met |
| Chapter 17 task by task, the migration of open actions to notices (E25) | `phase-5a.md` | Met in tests only; the migration from a build before the stack was not run on a real instance |

## Phase 6 · Hardening

| Criterion | Evidence | Result |
|---|---|---|
| Every pipeline runs with full scope in at most 25 minutes | 20.11.2026: CI 5 to 8 min, CodeQL 4 to 6 min, Release 10 min (run 44753516783, the 0.1.1 release), Acceptance 2 min, Website 1 min | Met |
| No open high-severity finding from 6.1 | `docs/security/threat-review.md`: open points are low or by design | Met |
| No open high-severity finding from 6.2 | The independent review has not taken place | **Open** |
| 6.3 manual accessibility pass, 6.4 indexes, memory and response times on the project's own instance, 6.8 the own instance | Not done (README roadmap ⬜). For 6.4 this run saw, after 13 minutes of first load: database 447 MB, worker 410 MB, API 165 MB resident; this is not the observation 6.4 asks for | **Open** |
| 6.5 restore and migrate test | `PreReleaseRestoreTests.Pre_release_dump_migrates_to_this_build_and_restores_again`; the update proxy above | Met |

## Phase 7 · Clean-up and release 1.0

| Criterion | Evidence | Result |
|---|---|---|
| Merging the release PR gives tag, `CHANGELOG.md`, signed images for both architectures and a GitHub release with notes, SBOM and Compose file, in at most 25 minutes | For 0.1.1 (#157): Release run 44753516783, 10 minutes, all of it present; for 1.0.0 not yet | Met for 0.1.1; **open** for 1.0.0 |
| Signature and provenance verify with the documented command for both architectures | Acceptance workflow, job «Signature and attestations»; cosign locally | Met |
| Every PR runs the pipeline and required checks in the branch protection block a merge without a green run | CI runs on every PR again (7.13). `main` has **no branch protection** (`GET …/branches/main/protection`: «Branch not protected») | **Open**: branch protection, with the owner (7.1) |
| A commit that is not a Conventional Commit fails the PR | `commits.yml` runs `build/check-commits.mjs` on every PR; locally the title «Fixed some stuff» fails with exit 1, a conventional one passes | Met |
| `seed-demo` gives an instance with data in under 2 minutes without GitHub or the sources, and aborts without change on a database with data | 15.6 s; refused twice (a worker-used instance and a seeded one), counts unchanged | **Met** (amd64) |
| A person new to the project installs Ardea from the README or website in under 15 minutes and signs in | Proxy: 2 minutes from an empty folder to a TOTP sign-in, automated | **Open**: needs a person |
| The website is republished on a release without manual work; link and accessibility checks find nothing | `site.yml` listens to `release: published`, but **no Website run has the event `release`**: the release is published by `release.yml` with the `GITHUB_TOKEN`, which starts no other workflow (bug 2). The site was rebuilt four minutes later only because #159 changed `docs/`. The link and accessibility checks pass in every Website run | **Not met** on release; checks met |
| All criteria of 1.6 and phases 0 to 5 on a fresh install for both architectures; this file has no open point | This file | **Open** (see below) |
| Build and lint without warnings; comment check; OpenAPI unchanged | CI on `main` green: `TreatWarningsAsErrors`, `ng lint`, the comment checks, «API client matches the OpenAPI document» | Met |
| GitHub holds only `main` and the branches of open PRs; a merged PR deletes its branch | At least 100 branches and no open PR; «Automatically delete head branches» is off (7.14 ⬜) | **Open** |
| Every UX/UI finding implemented or deferred; important actions visible, secondary in a menu; flows, visual comparison and screenshots green and current | 7.15 (#152: 41 implemented, 5 deferred, 2 rejected); CI green. On a phone the reminder's actions are not visible without scrolling to the end (bug 1) | Met, except bug 1 |
| After 7.12 no file links to `docs/masterplan/` | 7.12 waits for this task | **Open** |

## Chapter 1.6 · What the MVP is measured by

| Criterion | Evidence | Result |
|---|---|---|
| **First start**: `docker compose up -d` with the sample configuration (TLS proxy included) gives an instance ready for sign-in in under 3 minutes after the images are loaded; the first entries appear in under 10 minutes | Ready: 18 s (amd64 here), 13 s (amd64) and 16 s (arm64) in the workflow. First entries: 3 min 14 s (amd64 here); on arm64 not observed (the workflow does not wait for sources) | **Met** on amd64; ready-for-sign-in met on arm64, first entries on arm64 not observed |
| **Freshness**: a new or changed CVE record within 90 minutes, polled hourly | By design (`phase-1.md`) | Met by design; not observable in this run |
| **Completeness**: after an outage, catches up without manual work, by the change log up to 30 days, beyond by the archive; tested with 48 hours and a test for the archive path | Tests in `phase-1.md` | Met in tests only |
| **Deduplication**: the same CVE id from several sources is one entry with all sources | `Phase1AcceptanceTests.One_cve_from_every_source_is_one_entry_with_provenance_and_the_national_mark` | Met in tests only |
| **Match quality**: reference set thresholds | `ReferenceSetTests` (`phase-3.md`) | Met in tests only; labels await the owner's review |
| **Alert discipline**: an event processed three times gives one delivery per recipient and channel; an unchanged reminder triggers no second one | `NotificationStateTests` (`phase-5.md`) | Met in tests only |
| **Operation**: with the sample data, at 390 × 844 after opening a reminder, level, affectedness and first step visible without scrolling | On the release with `seed-demo`: «Critical», «Are we affected? Yes» and «What to do: Update to 8.4.27» end at 672 px of 844 | **Met** (amd64); see bug 1 for the actions |
| **Accessibility**: WCAG 2.2 AA, automated check without finding, manual keyboard and screen-reader check of the core flows passed | Automated: no finding on the release (above). Manual: not done | Automated **met**; manual **open** |
| **Without AI**: all criteria except the AI assessment hold without a provider | Every instance of this run had no provider | Met |

## The two manual passes of chapter 13.6

Chapter 13.6 keeps exactly two short passes by hand before a release. Neither was done; both need a person. The
script for both is [manual-passes.md](manual-passes.md).

| Pass | What covers it until then | Result |
|---|---|---|
| The core flows with keyboard and a screen reader (task 6.3) | axe in every Playwright flow and on every page of this walk; keyboard flows in `web/e2e/flows.spec.ts` (notices answered with 1 and 2, Esc, focus moving on; the reminder rhythm changed with the keyboard) | **Open** |
| Web Push on an installed web app on iOS | Chromium flow with a stubbed push manager; encryption against RFC 8291 and a fake push service (`phase-5.md`) | **Open** |

## Bugs found

Not fixed here; each needs its own pull request.

1. **On a phone, the reminder's action bar is not pinned to the bottom of the screen.** `action-page.ts` makes `.actionbar`
   `position: fixed; bottom: 0` below 640 px, but the shell's `.content` keeps the end state of its entry animation
   (`animation: shell-content-in 260ms … both`, keyframe `from { transform: translateY(6px) }`), so its computed
   transform stays `matrix(1, 0, 0, 1, 0, 0)`. Any transform makes `.content` the containing block of fixed
   descendants, and the bar sits at the end of the page instead of on the screen. With `prefers-reduced-motion` the
   animation is opacity only, and the bar is fixed as intended. Reproduce: `seed-demo` instance, a 390 × 844 viewport,
   open a reminder (`/o/example-digital/reminders/6`): «Mark as remediated» and «Other outcome» are not on the screen;
   `getBoundingClientRect().top` of `.ui-actionbar` is 1249 with `innerHeight` 844, and the ancestor `div.content` has a
   computed transform. The layout test «the three answers sit above the action bar» does not see it, because it only
   checks `barTop > 700`. Present in 0.1.1 and on `main`.
2. **The website is not republished on a release.** `site.yml` runs on `release: published`, but `release.yml` publishes
   the release with the `GITHUB_TOKEN` (`gh release edit … --draft=false`), and events made with that token start no
   workflow, the same reason 7.3 puts tag and images in one workflow. Reproduce: `gh run list --workflow site.yml --event
   release` is empty, while v0.1.1 was published at 14:36:57 UTC. The site showed the release only because a later docs
   push (#159) rebuilt it.
3. **Minor: the HTTP port redirects to port 443 when the public ports are moved.** With `ARDEA_HTTP_PORT_PUBLIC=8191` and
   `ARDEA_HTTPS_PORT_PUBLIC=8551`, `curl -I http://localhost:8191/` answers `308` to `https://localhost/` instead of
   `https://localhost:8551/`. Caddy builds the redirect from its own HTTPS port inside the container. Only installs that
   move the ports (`compose.caddy.yaml` documents the variables) are affected.
4. **Minor: `seed-demo` still says «actions».** Its summary reads «5 open and 2 done actions» (and the command's help
   «findings, actions»), while the product calls them reminders since 5a.
5. **Minor, documentation:** the install guide asks for «the GitHub CLI» for `gh attestation verify` without a minimum
   version; `gh` 2.45 (Ubuntu's package here) has no `attestation` command.

An observation, not a bug: on the overview the same PHP vulnerabilities appear as the precise reminder of the Webshop
(PHP 8.4.26) and as two notices on the stack's PHP entry, which has no version. This is how 17.1 and 17.4 describe the
two paths; the owner may want to look at it with the sample data.

## Open for 1.0

- **Run this acceptance on the 1.0 images:** `acceptance.yml` for the 1.0.0 version on both architectures, and the walk
  with `seed-demo` and live sources; the 1.0.0 release run itself (tag, `CHANGELOG.md`, images, notes, at most 25 minutes).
- **The update from the last `0.x` to 1.0** on an instance with data, as `docs/operations/update.md` says, including the
  way back. Until then: `PreReleaseRestoreTests` (6.5) on every build and the proxies above.
- **The two manual passes of 13.6:** keyboard and screen reader (6.3); Web Push on an installed web app on iOS, with one
  real push service each for Chrome, Firefox and Safari (`phase-5.md`).
- **Criteria that need a person:** the timed wizard runs of phase 2 (three example projects in under 10 minutes, also
  with a repository); a newcomer installing from the README or website in under 15 minutes.
- **6.2** independent security review; **6.4** indexes, memory and response times and **6.8** the project's own instance,
  which is also where digests, retries, reminders by level and notices are observed over weeks.
- **On arm64:** the pages with data and live sources were not walked; the 1.0 run should add `seed-demo` and the walk to
  the `arm64` job, or run them on an `arm64` host.
- **Carried from the phase records:** the live NCSC Cyber Security Hub and BACS fetches (failing from this network on
  19.11. and 20.11.2026; to check from the owner's network); reading Ardea's own repository; the owner's decisions on
  bundling transitive packages and on the reference set's labels (phase 3), the observation with a local model (phase 4),
  the reminder rhythm E26 and the open questions of `phase-5a.md`; the mockup reviews (E15).
- **Phase 7 with the owner:** branch protection with required checks (7.1, 7.13), branch clean-up and automatic deletion
  of head branches (7.14), the trademark search (E1), and deleting the master plan (7.12) once this list is empty.
- **The bugs above,** at least 1 and 2, fixed before 1.0.
