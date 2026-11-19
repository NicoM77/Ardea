# ADR 032: GitHub SBOM export and what Ardea reads itself (experiment 2.9)

- Status: accepted
- Date: 2026-11-19
- Refines: ADR 026 (profile from GitHub repositories)
- Master plan: 2.8, 16.2, 16.3, 16.4, 16.10, task 2.9

## Context

Master plan 16.10 left five questions about GitHub's Dependency Graph SBOM export open until an
experiment: the rights a token needs for the asynchronous export, whether public repositories answer
without a token, the SPDX version, whether the SBOM marks development dependencies and the dependency
path, how a private repository with the graph switched off answers, whether NuGet with central package
management is read, and the host the finished SBOM redirects to.

## Experiment

On 19.11.2026 against `api.github.com`, with read-only calls: a public repository without lockfile
(`expressjs/express`), a large public repository with central NuGet package management, npm lockfiles and
several ecosystems (`dotnet/aspire`, 11 531 packages, 7.2 MB), a private repository (`NicoM77/Ardea`)
and a repository that does not exist; each once with the session's token and the public ones once without.

| Question | Observed |
|---|---|
| Asynchronous flow | `GET …/dependency-graph/sbom/generate-report` → **201** with `{"sbom_url": ".../fetch-report/{uuid}"}`. `GET …/fetch-report/{uuid}` → **202** with `Retry-After: 1` and the body `{ }` while it is built, then **302** to the file. For the 11 531-package repository the second poll redirected. Reports are kept up to a week. |
| Download host | `dgpproduction.blob.core.windows.net` (Azure Blob, a signed URL with `rscd=attachment`). It needs no and must get no `Authorization` header. |
| Synchronous export | `GET …/dependency-graph/sbom` still answers 200, with `Deprecation: 12 May 2026` and `Sunset: 13 Nov 2026`. The documentation says it is closed; Ardea does not use it. |
| Without token | Public repositories answer the same 201 flow without a token, counted against the anonymous `core` limit of 60 requests per hour and IP. With a token the SBOM endpoints have their own bucket `dependency_sbom` of 100. |
| Rights | Fine-grained personal access token: repository permission **Contents: read**, the same as for branch head, tree and file contents (GitHub's permission table; the session had no fine-grained token, so it is confirmed in task 2.10 with a real one). A GitHub App needs the same. |
| SPDX | SPDX **2.3** JSON, created by `GitHub.com-Dependency-Graph` and `protobom`; every package carries a purl. |
| Development dependencies | **Not marked.** All 16 `devDependencies` of `express` are in the SBOM, indistinguishable from the 28 runtime ones. |
| Dependency path | Only where a lockfile gives it: without a lockfile every package `DEPENDS_ON` the repository (one source); with npm lockfiles 4 348 sources, i.e. transitive edges exist. |
| Versions | From a manifest only, ranges such as `^2.0.0`; from an npm lockfile exact (10 730 of 10 787). |
| NuGet, central package management | **Not resolved.** 226 of 266 NuGet packages carry `>= 0`, 21 none, 19 an exact version; the versions in `Directory.Packages.props` are not applied. |
| Private repository, graph off | `404`, the same as for a repository that does not exist; `GET /repos/{owner}/{repo}` answers 200 and Dependabot alerts are off. The dependency graph was not switched on for this private repository without action by its owner. |

## Decision

1. Ardea uses only the asynchronous flow: request, then poll `fetch-report` honouring `Retry-After`
   (at most a few minutes per run), treat **202 as "not yet", never as an empty SBOM**, and follow the
   302 itself to the download host without the token. A missing SBOM leaves the link `partial`, never
   an empty profile (16.5).
2. The HTTP guard of task 2.11 allows `api.github.com` (or the configured GitHub Enterprise Server host)
   and `dgpproduction.blob.core.windows.net`; redirects to other hosts are refused. The SBOM is limited
   in size (64 MB) and parsed streaming.
3. Tokens are fine-grained with **Contents: read** on the selected repositories, nothing else; the
   access page says so. Public repositories can be linked without a token, at the anonymous limit.
4. Ardea's own readers are not only a fallback but part of every reading:
   - `package-lock.json` gives exact versions, the `dev` flag and the path (direct or transitive);
   - `packages.lock.json` gives resolved NuGet versions, transitive ones included; without it,
     `Directory.Packages.props` with the project files gives the direct ones;
   - for the same purl, a lockfile's version and scope win over the SBOM's; a range or `>= 0` from the
     SBOM is "version unknown" (task 2.8 shows it as a coverage hint).
5. A private repository whose repository call answers 200 and whose SBOM request answers 404 is
   `partial` with the reason "dependency graph not enabled"; the wizard explains where to switch it on.
6. Requests send a pinned `X-GitHub-Api-Version`, checked in task 2.11.

## Consequences

- ADR 026 holds: the SBOM is the broad view across ecosystems, the lockfile readers make it precise.
- Development dependencies and the direct/transitive relation come from lockfiles; for ecosystems
  without an Ardea reader they stay `unknown` (16.5: a package without the flag counts as shipped).
- Open for task 2.10: confirm Contents: read with a real fine-grained token on a private repository
  with the graph switched on.
