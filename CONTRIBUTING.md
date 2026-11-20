# Contributing to Ardea

How Ardea is developed: the repository, the workflow and the rules for code, dependencies, tests and releases. How the
program is built is in the [architecture](docs/architecture.md); how to build and run it locally is under
«Development» in the [README](README.md#development).

## Repository layout

| Path | What it holds |
| --- | --- |
| `src/Hosts/Ardea.Host` | The one program: the roles `api`, `worker`, `migrate`, the helpers and the maintenance commands |
| `src/BuildingBlocks` | `Ardea.SharedKernel` (identifiers, results, base types), `Ardea.Platform` (jobs, schedules, tenant context, unit of work, audit, settings, secrets, telemetry), `Ardea.Ai` (language model providers) |
| `src/Modules/<Module>` | `Ardea.<Module>` and `Ardea.<Module>.Contracts` for Iam, Catalog, Assets, Intel, Assessment and Notifications |
| `src/Sources` | The provider SDK (`Ardea.Intel.Sources.Abstractions`, Apache-2.0), shared format readers (`Ardea.Intel.Sources.Formats`) and one project per source or source family |
| `web/` | The Angular workspace: the application (`projects/ardea`) and the component library (`projects/ui`) |
| `data/` | Master data shipped in the image: the catalogue (`catalog/`), the rule set (`rules/`) and the reference set (`reference/`) |
| `tests/` | `Ardea.<Module>.Tests` per module, `Ardea.Architecture.Tests` (module boundaries), `Ardea.Sources.Tests` (adapters against recorded answers), `Ardea.Host.Tests`, `Ardea.Platform.Tests`, shared helpers in `Ardea.TestSupport`, and the end-to-end flows in `e2e/` |
| `deploy/` | `Dockerfile`, `compose.yaml` with the Caddy variant, and the development Compose file |
| `build/` | Checks and scripts (see «Scripts»), the licence allow-list |
| `openapi/` | The committed OpenAPI document; a test keeps it equal to the code |
| `docs/` | Architecture, decision records (`adr/`), operation (`operations/`), security, acceptance records, brand and screenshots |
| `site/` | The website, built from `docs/` ([`site/README.md`](site/README.md)) |
| `tools/` | Scripts that select and rebuild the reference set |

## Language

Code, identifiers, commits, issues, pull requests and documentation are in English. The interface is English first;
other languages are translations through translation keys.

## Workflow

- One main branch, short-lived branches. Every change goes through a pull request with green checks.
  CI runs on every pull request and on every push to `main`, cancelling superseded runs of a pull request; changes
  to documentation alone (`docs/**`, `*.md`) run nothing. A pull request merges only with a green run.
- Commits follow [Conventional Commits](https://www.conventionalcommits.org/): `<type>(<optional scope>)!: <description>`
  with the type `feat`, `fix`, `perf`, `revert`, `docs`, `style`, `refactor`, `test`, `build`, `ci` or `chore`, and
  `!` or a `BREAKING CHANGE:` footer for a breaking change. Changes to the catalogue data are `feat(catalog):` or
  `fix(catalog):`. The *Commits* check (`.github/workflows/commits.yml`) fails a pull request whose title or one of
  whose commits does not follow them; merge commits and git's `Revert "…"` and `Reapply "…"` commits pass. Check a
  branch before pushing with `node build/check-commits.mjs` (range `origin/main..HEAD`, or base and head as
  arguments): a pushed branch is not rewritten.
- **The pull request title is a Conventional Commit, and it becomes the subject of the merge commit.** Pull requests
  are merged with a merge commit (no squash, no rebase), and its subject is the title with the number:
  `gh pr merge <N> --merge --subject "<title> (#<N>)" --body "<branch>"`, or the merge button once the repository's
  default merge message is the pull request title with an empty body. The changelog relies on it: release-please
  reads `main`'s history by date and stops at the last release, so a branch commit written before the last release
  but merged after it is found only through the merge commit's subject. The body must not hold a second Conventional
  Commit; a breaking change takes the `!` in the title.
- Decisions with lasting impact get an ADR in `docs/adr/`: context, decision, alternatives, consequences. Short,
  dated and numbered.
- Vertical slices: a feature is finished through every layer before the next one starts. Nothing is built in advance.

### Done means

- The task's acceptance criteria are met and shown: by a fast test where possible, otherwise by an observation
  recorded once during development.
- Every pipeline stays within its limit (see «Pipelines»).
- No new finding in build, lint, analyzers, the licence check or the accessibility check.
- Migrations are forward-compatible, master data is up to date.
- The interface is complete in English, every state is there, and it works with the keyboard.
- Documentation and, where concerned, an ADR are updated.
- The new path has telemetry.

## Code

- `Nullable` and `TreatWarningsAsErrors` are on; formatting follows `.editorconfig` (`dotnet format`). Unused code
  fails the build: unused usings, private members, fields, parameters and assignments, and unreachable code
  (IDE0005, IDE0051, IDE0052, IDE0059, IDE0060, CA1823, IDE0035, CS0162).
- Central package management (`Directory.Packages.props`) with lock files; the SDK is pinned in `global.json`.
- Types of a module are `internal`. Only the module entry point (`*Module`) is public; whatever other modules
  may use lives in the module's `Contracts` assembly. The architecture tests enforce this.
- A module uses another module only through its `Contracts`. The allowed directions are in
  [Rules between modules](docs/architecture.md#rules-between-modules).
- Domain logic does not depend on EF Core, HTTP or the clock; time comes from `TimeProvider`.
- Asynchronous all the way, with a `CancellationToken` passed into every I/O call.
- Logging through generated log methods with fixed event ids; never profile contents or secrets.
- No mediator, no object mapper: endpoints call handlers directly, mappings are written in code.
- Migrations go forward only and change the schema compatibly: first extend, remove what is no longer used one
  version later, so the previous build keeps running against the new schema during an upgrade.

### Web app

- TypeScript in strict mode; standalone components, signals, no zone.js.
- The API client is generated from the OpenAPI document by orval and never edited by hand; CI checks that it
  matches the document.
- Colours, spacing and type only through the design tokens of [`DESIGN.md`](DESIGN.md); no colour values in
  components (`npm run check:tokens`).
- Every component brings its states: default, hover, focus, disabled, loading, error, empty.
- Every text goes through a translation key (`npm run check:i18n`).

### Comments

Code carries no comments (master plan 14, task 7.5): names, types and tests say what the code does, and the reasons
live in the master plan, the ADRs and the pull requests. `build/check-comments.sh` fails on every comment outside
the exceptions below; `build/check-comments.sh --fix` removes them (run `dotnet format` and `npm run lint` and review the diff after). The C# part is a Roslyn tool
(`build/Ardea.CommentCheck`), the web part uses the TypeScript compiler (`build/check-comments.mjs`, `npm run
check:comments` in `web/`), so strings and URLs are never mistaken for comments.

Checked: C# in `src/`, `tests/` and `build/`; SQL comments (`--`, `/* */`) in SQL inside C# string literals (an
argument of a method whose name contains `Sql`, or a raw or verbatim literal that starts with a statement keyword);
shell scripts (`*.sh`); TypeScript and JavaScript in `web/`, `site/`, `tests/e2e/` and `build/`; HTML templates, also inline
`template:`; CSS and SCSS, also inline `styles:`, in `web/` and `site/`. Not checked: configuration and data (YAML, JSON, MSBuild files, `Dockerfile`, `Caddyfile`,
`.editorconfig`), Markdown, Astro components (`site/src/**/*.astro`), and the Python scripts.

Exceptions, and nothing else:

| Exception | Rule |
| --- | --- |
| XML documentation the OpenAPI document takes its descriptions from | A top-level element of a `///` comment (`<summary>`, `<param>`, …) whose text is a description in the committed `openapi/ardea.v1.json`. `<c>x</c>` reads as `` `x` ``, `<see …/>` and similar as any text. The contract test keeps the file equal to the code, so a description on a new endpoint type passes once the document is regenerated (`ARDEA_UPDATE_OPENAPI=1`). |
| Justification of a suppressed warning | A comment on the line of a `#pragma warning disable`, or the comment lines directly above it; the same for `eslint-disable…` (which also takes a reason after ` -- `), `stylelint-disable…` and `prettier-ignore`. `[SuppressMessage]` takes its reason as `Justification`. |
| Tool directives | `// <auto-generated`, `// ReSharper disable` / `restore`, `// language=`; in shell scripts the `#!` line and `# shellcheck` directives; in TypeScript and JavaScript `// @ts-check`, `// @ts-nocheck`, `// @ts-expect-error <reason>`, `/// <reference …/>` and `istanbul` / `c8` / `v8 ignore`. |
| Generated code | EF Core's `*.Designer.cs` and `*ModelSnapshot.cs`; the scaffolded `/// <inheritdoc />` in migrations; the web API client generated by orval (`web/projects/ardea/src/app/core/api/generated/`). |

## Dependencies

A new dependency needs a reason in its pull request. Its licence must be on the allow-list in
`build/licenses/allowed-licenses.json` (MIT, Apache-2.0, BSD-2/3-Clause, ISC, PostgreSQL, MPL-2.0, OFL-1.1).
Excluded because they are commercial or impose conditions on operators: MediatR, AutoMapper, MassTransit 9+,
FluentAssertions 8+, ImageSharp, Duende IdentityServer, TickerQ, PrimeNG 22+, Verify 33+ (snapshot tests; build-time maintenance-fee check).
«Free for open-source projects» is not enough: such a clause covers the repository, not the organisation that runs
Ardea.

## Tests

Every pipeline runs in 15 to at most 25 minutes. Many fast unit tests, few tests against the real database,
a handful of end-to-end tests for the core flows. No long-running and no scheduled tests.

A test checks a rule or a defect that can be named, and its name says which. It sits at the lowest level where
the statement is possible: what a unit test can show is not checked again through the interface. There is no
coverage quota, and no tests for mappings without logic or for the behaviour of frameworks and libraries. A defect
found in operation becomes a test, with the raw record that caused it, before it is fixed.

| Level | What | Tools |
| --- | --- | --- |
| Unit | Version comparison, product keys, precedence rules, the rule set with caps and floors, fingerprints, reminders, notification state, reconciling repository components, merging entries; in the web app the logic and states of components | xUnit v3, Vitest |
| Adapters | Every source adapter against recorded real answers; the GitHub connection, every lock-file reader and detection rule against real files and SBOMs; output compared as a snapshot | xUnit v3, own snapshot comparison, no network |
| Reference set | Matching and levels against the labelled pairs in `data/reference/`, rules only, with the thresholds as acceptance ([Rule set and levels](docs/architecture/rule-set.md#measuring-the-rules)) | xUnit v3 against the real pipeline |
| Integration | Only what the real database shows: idempotence of the job chain, retries and leases, the shared transaction, tenant isolation, migrations | Testcontainers, one PostgreSQL server per run |
| Architecture and contract | Module boundaries; the OpenAPI document equals the code; a cross-tenant test for every organisation endpoint | ArchUnitNET, own tests |
| Web flows | The core flows of the web app against a mock API, the pages under the production CSP with axe, and the visual comparison of the component library | Playwright, axe |
| End to end | Flows against a real instance started from the image as an operator does (`tests/e2e/run.sh`); today the first sign-in with setup token, password and TOTP | Playwright |

A new end-to-end test needs a reason in its pull request and, in doubt, replaces an existing one. Nothing runs
outside a pipeline: no load tests, no long runs, no nightly or scheduled runs, no runs against real sources or real
language models. What cannot be a fast test is not kept as a test: acceptance criteria about time and volume are
observed once and recorded; a source that changes its structure shows in operation (quarantine and empty fetches mark
it degraded); the quality of a language model shows as the correction rate in operation.

Before a release two short passes stay manual: the core flows with keyboard and screen reader, and Web Push on an
installed web app on iOS.

### Measuring coverage

Coverage shows where a rule may lack a test; it is not a goal and the pipeline does not measure it.
`build/coverage.sh` runs one test project under [coverlet](https://github.com/coverlet-coverage/coverlet)
(`coverlet.console` from the local tool manifest, MIT) and prints line and branch coverage per assembly:

```sh
build/coverage.sh Ardea.Catalog.Tests '[Ardea.Catalog]*' '[Ardea.Catalog.Contracts]*'
build/coverage.sh Ardea.Iam.Tests        # every Ardea assembly the tests load
```

The report for the uncovered lines is `artifacts/coverage/<TestProject>.cobertura.xml`, which IDEs and report
tools read. Migrations and generated code are left out. Tests against the database need Docker, as for `dotnet test`.

## Pipelines

| Pipeline | What | Target | Limit |
| --- | --- | --- | --- |
| Pull request | build, format, lint, comments, licences, all tests, the web flows under CSP, end to end, image for `amd64` with a smoke test | 15 minutes | 25 minutes |
| `main` | the same, plus the visual comparison and the `arm64` image | 20 minutes | 25 minutes |
| Release | images for both architectures, signed, with SBOM and notes | 20 minutes | 25 minutes |

The jobs run side by side, with dependencies and build layers from the cache. The `arm64` image is cross-compiled, not
emulated. Integration tests share one PostgreSQL server per run and create databases in it instead of starting
servers. A pipeline over its limit is a defect to fix by tightening, moving or removing tests; the limit is not
raised.

## Releases

Releases are made by [release-please](https://github.com/googleapis/release-please) in
`.github/workflows/release.yml` ([ADR 050](docs/adr/0050-releases-with-release-please-and-signed-images.md)).
Versions follow Semantic Versioning. A major version is needed for a break in the API `v1`, in the environment
variables, or in stored data without an automatic migration; before 1.0 such a break raises the minor version.

**Before the first release (owner, repository settings):**

- [ ] *Settings → Actions → General → Workflow permissions:* «Allow GitHub Actions to create and approve pull
      requests» on. Without it release-please cannot open the release pull request.
- [ ] The repository is public. GitHub artifact attestations (provenance and SBOM) need a public repository or
      GitHub Enterprise Cloud, and users must be able to pull the image.
- [ ] *Settings → General → Pull Requests:* «Allow merge commits» with the default message «Pull request title»
      (empty body); «Allow squash merging» and «Allow rebase merging» off (see «Workflow» above).
- [ ] After the first release: the package `ghcr.io/nicom77/ardea` is linked to the repository and public
      (*Packages → ardea → Package settings*).

The flow:

1. Every push to `main` updates the **release pull request** (`chore(main): release X.Y.Z`): the next version from the
   commits since the last release (`fix` a patch, `feat` a minor version, a breaking change the major version, before
   1.0 the minor version) and its section in `CHANGELOG.md`. Only `feat`, `fix`, `perf` and `revert` appear there and
   open a release pull request; the other types do not. A pull request can appear twice, by its merge commit and by
   its own commits. Never edit `CHANGELOG.md` or `.release-please-manifest.json` by hand; a commit with a
   `Release-As: X.Y.Z` footer sets the next version.
2. **The owner merges it** (with a merge commit, as any pull request) when a release is due; nobody else does. The
   pull request is opened by the workflow's token, so no check starts on it by itself: close and reopen it once to
   run them.
3. The merge creates the tag `vX.Y.Z` and a draft release. The same workflow then builds `linux/amd64` and
   `linux/arm64` with the version and pushes them to `ghcr.io/nicom77/ardea` by digest only, without a tag; requires
   `ardea version` to print the version and the *CI* run on the release commit to be green (it waits up to 25
   minutes for it); signs the images with cosign (keyless) and attests provenance and SBOM; attaches SBOMs, notices,
   `compose.yaml`, `compose.caddy.yaml` and `Caddyfile`; and only then sets the tags `X.Y.Z`, `X.Y`, `X` (`latest` from
   1.0 on) and publishes the release, as a pre-release while the version is 0.x. About 20 minutes, at most 25 when
   CI keeps its own budget.
4. If a job fails, the release stays a draft and no image tag moves.
   - A passing cause (CI re-run green, a registry or network error): *Re-run failed jobs* on the run. *Re-run all
     jobs* does nothing: release-please finds the release already tagged and creates nothing.
   - Started by hand, *Actions → Release → Run workflow* on `main` with an existing tag (`vX.Y.Z`) builds, signs,
     attests and publishes that tag again, for example after the run's logs expired.
   - A defect in the released code: delete the **draft release only** and keep the tag `vX.Y.Z` (release-please
     takes the tag as the last release, so the next changelog does not repeat this one), then release the fix as
     the next version. A deleted tag would make release-please start again from `bootstrap-sha`.

Local builds report the version `dev`; `docker build --build-arg VERSION=X.Y.Z` gives an image a version.

## Scripts

| Script | What it does |
| --- | --- |
| `build/check-comments.sh [--fix]` | Comments outside the exceptions (see «Comments»): the C# tool, then the web part. |
| `node build/check-commits.mjs [base] [head]`, `--title "<title>"` | Every commit in `base..head` (default `origin/main..HEAD`) follows Conventional Commits, merge commits pass; or a pull request title does. The *Commits* check of a pull request runs both. |
| `npm run build` (in `site/`) | The website: copies the pages from `docs/`, `SECURITY.md`, `CONTRIBUTING.md` and the API reference from `openapi/ardea.v1.json` into Astro with Starlight and builds `site/dist/`; a link from those pages to a file that does not exist fails it. Sources and publishing in [`site/README.md`](site/README.md). |
| `npm run check:links` (in `site/`) | Every internal link and anchor of the built site, landing page and API reference included. |
| `npm run check:a11y` (in `site/`) | axe (WCAG 2.2 AA tags) with Playwright on the landing page, every documentation page and a sample of API operations; runs in the Playwright image like the web app's checks (command in `site/README.md`). |
| `npm run check:licenses` (in `site/`) | The web app's licence check (`web/scripts/check-licenses.mjs`) on the website's production packages; exceptions with reasons in `site/license-exceptions.json`. |
| `npm run check:unused` (in `web/`) | [knip](https://knip.dev) (ISC): unused files, exports, types and dependencies of the web app; configuration in `web/knip.json`. Translation keys nobody uses fail `npm run check:i18n`. |
| `build/check-licenses.sh` | Licence gate for every direct and transitive NuGet package (allow-list in `build/licenses/`). |
| `build/coverage.sh <TestProject> [<include filter> …]` | Line and branch coverage of one test project (see «Measuring coverage»). |
| `build/sbom.sh [output directory]` | CycloneDX SBOMs of the .NET host and the web app's production packages (default `artifacts/sbom`); needs the restored solution and `npm ci` in `web/`. |
| `build/third-party-notices.sh [output file]` | `THIRD-PARTY-NOTICES.md` of everything the image ships (default `artifacts/THIRD-PARTY-NOTICES.md`); `NPM_NOTICES=<file>` takes the npm part from a file the image build already wrote. |
| `build/smoke-test.sh <image>` | Starts an instance from `deploy/compose.yaml` and checks it is healthy within 3 minutes, serves the web app and answers the API as expected; `SMOKE_KEEP=1` leaves it running. |
| `build/screenshots.sh [image]` | Builds the image (or takes the one given), starts an instance from `deploy/compose.yaml` under its own Compose project, loads the demo data with `seed-demo`, takes the images in `docs/screenshots/` with Playwright in its official image (`tests/e2e/screenshots/`) and removes the instance with its volumes. The demo owner's sign-in stays in the script's memory. `SCREENSHOTS_API_PORT` sets the API's port on 127.0.0.1 (default 18090), `SCREENSHOTS_KEEP=1` leaves the instance running. Needs Docker only. |
| `tests/e2e/run.sh <image>` | Starts an instance as an operator does (with Caddy, HTTPS on `localhost`) and runs the end-to-end flows in the Playwright image. `E2E_HTTPS_PORT` / `E2E_HTTP_PORT` set the published ports (default 443 / 80), `E2E_API_PORT` the API's port on 127.0.0.1 (default 18081), `E2E_KEEP=1` leaves the instance running, and `E2E_WEB_DIST=web/dist/ardea/browser` serves a fresh web build instead of the image's. Needs Docker and `npm ci` in `tests/e2e`. |
