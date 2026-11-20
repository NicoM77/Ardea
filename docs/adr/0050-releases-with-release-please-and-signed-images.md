# ADR 050: Releases with release-please, signed images for two architectures

- Status: accepted
- Date: 2026-11-20
- Refines: ADR 002 (one image), ADR 025 (time budget), ADR 042 (SBOM)
- Master plan: 12.3, 12.6, 13.7, tasks 7.2 and 7.3

## Context

Chapter 12.3 asks for images for `linux/amd64` and `linux/arm64` with the tags `1.4.2`, `1.4`, `1` and `latest`, an SBOM
per release and images signed with cosign and attested with build provenance. Task 7.2 adds versioning from
Conventional Commits with a release pull request, a check that lets only Conventional Commits into a pull request, and
the version in the image, `doctor` and the administration; task 7.3 builds the images «from the checked state». A tag
created with the workflow's `GITHUB_TOKEN` starts no other workflow, so the release pipeline must run in the workflow
that creates the tag. The only secrets available are `GITHUB_TOKEN` and the workflow's OIDC identity. Pull requests are
merged with merge commits, and pushed branches are not rewritten.

## Decision

1. **release-please, one product version.** `release-please-config.json` and `.release-please-manifest.json` describe one
   package at the repository root with the `simple` strategy: `CHANGELOG.md` and the manifest are the only files a
   release pull request changes. The .NET projects and `web/package.json` keep `0.0.0`; the version reaches the build
   only as the Docker build argument `VERSION` (`-p:Version`). The first release is `0.1.0`; the history before
   `bootstrap-sha` (the main branch when this was set up) stays out of the changelog. Features, fixes, performance and
   reverts appear in the changelog and trigger a release pull request; `docs`, `refactor`, `test`, `build`, `ci`,
   `chore` and `style` do not. Before 1.0 a breaking change raises the minor version (`bump-minor-pre-major`).
2. **0.x is a pre-release.** `prerelease: true` marks a release as pre-release while the major version is 0 (release-please
   applies it only then), so GitHub names no 0.x release «latest», and the image tag `latest` is set only from 1.0 on.
   A 0.x release gets `0.x.y`, `0.x` and `0`; from 1.0 on `X.Y.Z`, `X.Y`, `X` and `latest`; a version with a
   pre-release suffix only its full tag.
3. **The merge commit's subject is a Conventional Commit.** release-please walks `main`'s history in date order and
   stops at the commit of the last release. A branch commit written before the last release but merged after it lies
   beyond that point and would be lost. The merge commit has the date of the merge, so its subject carries the pull
   request into the changelog: pull request titles must be Conventional Commits (checked), and the merge commit's
   subject is the title with the number (`gh pr merge N --merge --subject "<title> (#N)"`, or the repository's default
   merge message set to the pull request title with an empty body). release-please parses a commit by its message
   alone (`parseConventionalCommits` in v17.11.2 has no special case for merges; GitHub's default `Merge pull request
   …` subject simply does not parse), so such a merge commit counts like any other. The price: a pull request whose own
   commits are newer than the last release appears twice, once by title and once by its commits. Rebasing or squashing
   would avoid that but would rewrite pushed branches or lose the commits' history.
4. **Pushed by digest, tagged last.** Merging the release pull request makes release-please create the tag (forced,
   `force-tag-creation`) and a draft release. `.github/workflows/release.yml` then:
   - builds both architectures with the release version and pushes them **by digest only** (the SDK stage
     cross-compiles, the runtime stage runs no command, so no emulation); the `amd64` image must print the version;
   - in parallel waits for a green *CI* run on the release commit (or, when that commit changed documentation only,
     which CI skips, on its first parent), up to 25 minutes; CI's image job runs the smoke test on that commit;
   - after both: signs the index and both platform manifests with cosign without a key (`--recursive`, the
     workflow's OIDC identity) and attests build provenance for the index and each platform digest;
   - builds the two CycloneDX SBOMs of ADR 042 in parallel, attests them for the index, attaches them,
     `THIRD-PARTY-NOTICES.md`, `compose.yaml`, `compose.caddy.yaml` and `Caddyfile` to the release;
   - only then sets the tags with `docker buildx imagetools create` (the full version first, checked to point at the
     signed digest, then the moving tags) and publishes the release.
   A failed run leaves a draft and moves no tag, so `:0` never points at an unsigned image. *Re-run failed jobs*
   finishes it. Started by hand on `main` with an existing tag, the workflow builds, signs, attests and publishes that
   tag again; the start from `main` keeps the signing identity.
5. **Recovery keeps the tag.** A release with a defect is abandoned by deleting the draft release only. release-please
   finds the last release by its tag as well, so the next changelog starts after it; deleting the tag would send
   release-please back to `bootstrap-sha` and repeat the earlier entries.
6. **One workflow, least privilege per job.** release-please (`contents`, `issues`, `pull-requests: write`), CI gate
   (`actions`, `contents: read`), SBOM (`contents: read`), images (`packages: write`), signing (`packages`,
   `id-token`, `attestations: write`) and publishing (`contents: write` plus what the SBOM attestations need). The
   workflow default is no permission. Every action is pinned by commit SHA. No cache is read in the release jobs.
7. **Conventional Commits in pull requests.** `build/check-commits.mjs` (Node, no dependency) checks the title and every
   commit of the pull request (`HEAD^1..HEAD^2` of the checked-out merge ref); merge commits and git's `Revert "…"` and
   `Reapply "…"` commits pass. `.github/workflows/commits.yml` runs it on `pull_request` (also when the title is
   edited) with a read-only token; the title reaches the script through the environment, never through the shell.
8. **`dev` for builds without a version.** `AppVersion` reports the informational version without build metadata, and
   `dev` when it is `0.0.0` or missing. `ardea version`, `ardea doctor` (first line), the administration, the worker's
   heartbeat and telemetry all show the same value.

## Alternatives

- **semantic-release:** releases on every merge without a pull request to review; the plan asks for a release pull
  request.
- **Squash merging** for a one-to-one changelog: loses the branch commits and contradicts the merge-commit practice.
- **`changelog-type: github`** (GitHub's generated notes by pull request): no sections by type, and the first release
  would list every pull request since the start.
- **QEMU for arm64:** emulating the whole build would take several times as long and break the time budget of 13.7.
- **A personal access token or GitHub App** so the tag starts a second workflow: a secret besides `GITHUB_TOKEN`,
  which the plan excludes.
- **Build provenance from BuildKit alone** (`provenance: mode=max`): not checkable with `gh attestation verify` and it
  adds `unknown/unknown` entries to the index.
- **Pushing the tags with the build:** a failed signing step would leave `:0` on an unsigned image.
- **A smoke test of its own in the release run:** CI's image job already runs it on the same commit; waiting for CI
  covers it and every other check.
- **Version files in the projects** updated by release-please: local builds would then claim a release version.

## Consequences

- About 20 minutes from the merge to the published release: the build (about 9) runs while the workflow waits for CI
  (15 by its own target), then signing 2 and publishing 2. A slow CI run makes the release slower, not unchecked.
- The release pull request is opened with `GITHUB_TOKEN`, so no workflow runs on it by itself; the owner closes and
  reopens it once to start the checks. *Settings → Actions → General* must allow GitHub Actions to create pull
  requests.
- GitHub artifact attestations need a public repository (or GitHub Enterprise Cloud); the first release presupposes
  the repository and the package `ghcr.io/nicom77/ardea` to be public.
- Squash and rebase merging stay switched off in the repository settings, and merges follow decision 3.
- At 1.0, `deploy/compose.yaml` and the documentation move from `:0` to `:1` in the release pull request; the
  `prerelease` setting stays, it no longer applies.
- Verifying uses cosign 3 or later and the GitHub CLI; the commands are in `docs/operations/installation.md` and pin the
  identity to `release.yml` on `refs/heads/main`.
