# Fixtures of the repository readers (task 2.11)

Real files, trimmed to keep the repository small. The tests assert counts on them, so do not refresh
them without updating the tests.

| File | Origin | Trimmed |
|---|---|---|
| `web-package-lock.json` | `web/package-lock.json` of this repository (lockfile v3, 835 packages, 763 distinct name and version), 19.11.2026 | per package: `integrity`, `resolved`, `license`, `funding`, `engines`, `bin`, `cpu`, `os`, `libc` removed; structure, `dev` flags and line layout of the keys kept |
| `host-packages.lock.json` | `src/Hosts/Ardea.Host/packages.lock.json` of this repository, 19.11.2026 | unchanged |
| `express-sbom.json` | GitHub dependency graph SBOM of `expressjs/express` (no lockfile in the repository), recorded on 19.11.2026 with the asynchronous export: `generate-report` → `fetch-report` (202, then 302) → download from `dgpproduction.blob.core.windows.net` | `licenseConcluded`, `licenseDeclared`, `copyrightText` removed; all 54 packages and relationships kept |
| `checkout-sbom.json` | GitHub dependency graph SBOM of `actions/checkout` (npm lockfile v3, so the SBOM carries dependency paths), recorded the same way on 19.11.2026 | licence and copyright fields, `downloadLocation` and `filesAnalyzed` removed; only packages up to two edges from the root (126 of 566) and the relationships between them kept |

Re-record an SBOM with `gh api repos/{owner}/{repo}/dependency-graph/sbom/generate-report`, then
`gh api -i` on the returned `sbom_url` until it answers 302, then download the `Location` without a token.
