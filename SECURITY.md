# Security policy

## Reporting a vulnerability

Please report security issues in Ardea **privately** through GitHub: open the repository's
**Security** tab and choose **Report a vulnerability**. Do not open a public issue for them.

Include what you found, how to reproduce it, the version (`ardea version`) and the impact you see. Reports are
acknowledged as soon as possible; fixes for confirmed issues are released when they are ready and credited if
you wish.

Ardea itself warns about vulnerabilities in other software; reports about those belong to the respective
projects, not here.

## How Ardea protects itself

The [threat review](docs/security/threat-review.md) goes through every threat of the master plan (chapter 9.7)
with its mitigation in the code and the test that proves it, and lists what is still open. Reviewers start
there; operators find the settings that matter (master key, trusted proxies, rate limits) in the
[configuration](docs/operations/configuration.md).

## Supported versions

The latest release of the current major version receives security fixes.

## How operators learn about security updates

- Security fixes are published as **GitHub security advisories** of this repository and in the release
  notes; watch the repository with «Releases» and «Security alerts» selected.
- From the first release on, images are tagged by version (`ghcr.io/nicom77/ardea:0`, `:0.x`, `:0.x.y`);
  `docker compose pull` with the major tag brings fixes in. See [Update](docs/operations/update.md).
