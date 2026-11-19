# ADR 042: SBOM and third-party notices

- Status: accepted
- Date: 2026-11-19
- Refines: ADR 002 (one image), ADR 028 (runtime image)
- Master plan: 12.3, 13.5, task 6.8

## Context

Chapter 12.3 asks for an SBOM per release and chapter 13.5 for a licence gate on dependencies; the gate
(`build/check-licenses.sh`, `web/scripts/check-licenses.mjs`) existed, but nothing produced a bill of materials, and
the image shipped neither Ardea's licence nor the licences of what it contains. MIT, BSD and Apache-2.0 packages
oblige whoever distributes them to keep their notices with the distribution; the image is such a distribution.
Signing and publishing the images belong to task 7.3 and need the owner's infrastructure.

## Decision

1. **CycloneDX JSON, two files.** `build/sbom.sh` writes `ardea-dotnet.cdx.json` (the `dotnet-CycloneDX` tool, pinned
   in `.config/dotnet-tools.json`, over the host project and the projects it references, without test projects) and
   `ardea-web.cdx.json` (`@cyclonedx/cyclonedx-npm`, pinned in `web/package.json`, production packages only). It runs
   locally and in the `dependencies` job of CI on `main`, which keeps the result as an artefact. CycloneDX is a
   standard both ecosystems' tools write natively; the files are not merged, so each stays what its tool produced.
2. **One notices file in the image.** `build/third-party-notices.sh` writes `THIRD-PARTY-NOTICES.md`: the package,
   version, licence expression, project address and copyright of every NuGet package (from `nuget-license`, the tool
   of the licence gate, with the same overrides) and the licence text of every production npm package. The Dockerfile
   builds it from the restored packages, so it always matches what the image contains, and copies it with `LICENSE`
   to `/app`. A committed file would go stale with every dependency change.
3. **Local first.** Both scripts need only the .NET SDK and `npm ci`; `docker build -f deploy/Dockerfile .` produces
   the notices without extra steps, so a developer checks the result before CI does.

## Consequences

- The SBOM is a build artefact until task 7.3 attaches it to a release and to the signed images; the image itself
  carries the notices, not the SBOM.
- Adding a dependency changes both outputs without further work. A package whose licence the tool cannot read needs
  an entry in `build/licenses/overrides.json`, as for the gate.
- Development-only packages (test, build tools) are not in the notices; they are not shipped.
- The project's own instance (feeding Ardea from its own repository) needs the owner's infrastructure and stays open.
