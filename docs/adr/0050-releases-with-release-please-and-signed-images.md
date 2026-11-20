# ADR 050: Releases with release-please, signed images for two architectures

- Status: accepted
- Date: 2026-11-20
- Refines: ADR 002 (one image), ADR 025 (time budget), ADR 042 (SBOM)
- Master plan: 12.3, 12.6, 13.7, tasks 7.2 and 7.3

## Context

Chapter 12.3 asks for images for `linux/amd64` and `linux/arm64` with the tags `1.4.2`, `1.4`, `1` and `latest`, an SBOM
per release and images signed with cosign and attested with build provenance. Task 7.2 adds versioning from
Conventional Commits with a release pull request, a check that lets only Conventional Commits into a pull request, and
the version in the image, `doctor` and the administration. A tag created with the workflow's `GITHUB_TOKEN` starts no
other workflow, so the release pipeline must run in the workflow that creates the tag. The only secrets available are
`GITHUB_TOKEN` and the workflow's OIDC identity.

## Decision

1. **release-please, one product version.** `release-please-config.json` and `.release-please-manifest.json` describe one
   package at the repository root with the `simple` strategy: `CHANGELOG.md` and the manifest are the only files a
   release pull request changes. The .NET projects and `web/package.json` keep `0.0.0`; the version reaches the build
   only as the Docker build argument `VERSION` (`-p:Version`). The first release is `0.1.0`; the history before
   `bootstrap-sha` (the main branch when this was set up) stays out of the changelog. Features, fixes, performance and
   reverts appear in the changelog and trigger a release pull request; `docs`, `refactor`, `test`, `build`, `ci`,
   `chore` and `style` do not. Before 1.0 a breaking change raises the minor version (`bump-minor-pre-major`).
2. **0.x is a pre-release.** `prerelease: true` marks a release as pre-release while the major version is 0, so GitHub
   names no 0.x release «latest», and the image tag `latest` is pushed only from 1.0 on. A 0.x release gets `0.x.y`,
   `0.x` and `0`; from 1.0 on `X.Y.Z`, `X.Y`, `X` and `latest`; a version with a pre-release suffix only its full tag.
3. **Draft first, published last.** Merging the release pull request makes release-please create the tag (forced,
   `force-tag-creation`) and a draft release. `.github/workflows/release.yml` then builds an `amd64` image with the
   release version, requires `ardea version` to print it and the smoke test to pass, builds and pushes both
   architectures (the SDK stage cross-compiles, the runtime stage runs no command, so no emulation), signs the index and
   both platform manifests with cosign without a key (`--recursive`, the workflow's OIDC identity), attests build
   provenance for the index and each platform digest and the two CycloneDX SBOMs of ADR 042 for the index, attaches the
   SBOMs, `THIRD-PARTY-NOTICES.md`, `compose.yaml`, `compose.caddy.yaml` and `Caddyfile` to the release, and only then
   publishes it. A failed run leaves a draft that is never visible to users; «Re-run failed jobs» finishes it.
4. **One workflow, least privilege per job.** release-please (`contents`, `issues`, `pull-requests: write`), SBOM
   (`contents: read`), images (`packages`, `id-token`, `attestations: write`) and publishing (`contents: write` plus
   what the SBOM attestations need). The workflow default is no permission. Every action is pinned by commit SHA. No
   cache is read in the release jobs.
5. **Conventional Commits in pull requests.** `build/check-commits.mjs` (Node, no dependency) checks every commit
   between the pull request's base and head; merge commits and GitHub's `Revert "…"` commits pass.
   `.github/workflows/commits.yml` runs it on `pull_request` with a read-only token. The repository merges with merge
   commits, so the pull request title is not checked.
6. **`dev` for builds without a version.** `AppVersion` reports the informational version without build metadata, and
   `dev` when it is `0.0.0` or missing. `ardea version`, `ardea doctor` (first line), the administration, the worker's
   heartbeat and telemetry all show the same value.

## Alternatives

- **semantic-release:** releases on every merge without a pull request to review; the plan asks for a release pull
  request.
- **QEMU for arm64:** emulating the whole build would take several times as long and break the time budget of 13.7.
- **A personal access token or GitHub App** so the tag starts a second workflow: a secret besides `GITHUB_TOKEN`,
  which the plan excludes.
- **Build provenance from BuildKit alone** (`provenance: mode=max`): not checkable with `gh attestation verify` and it
  adds `unknown/unknown` entries to the index.
- **Version files in the projects** updated by release-please: local builds would then claim a release version.

## Consequences

- The time budget of 25 minutes holds with about 18 expected: release-please 1, images about 13 (smoke test included),
  SBOM in parallel, publishing 2.
- The release pull request is opened with `GITHUB_TOKEN`, so no workflow runs on it by itself; with required checks the
  owner closes and reopens it once (or pushes to its branch) to start them. *Settings → Actions → General* must allow
  GitHub Actions to create pull requests.
- GitHub artifact attestations need a public repository (or GitHub Enterprise Cloud); the first release presupposes
  the repository and the package `ghcr.io/nicom77/ardea` to be public.
- At 1.0, `deploy/compose.yaml` and the documentation move from `:0` to `:1` in the release pull request; the
  `prerelease` setting stays, it no longer applies.
- Verifying uses cosign 3 or later and the GitHub CLI; the commands are in `docs/operations/installation.md`.
