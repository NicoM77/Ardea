# ADR 031: OSV source for six package ecosystems

- Status: accepted
- Date: 2026-11-18
- Refines: ADR 027 (OSV for six package ecosystems in the MVP)
- Master plan: 5.3, 5.4, 5.6, 6.1, 6.2, 6.6, 16, task 1.12

## Context

Projects read their dependencies from repositories (chapter 16); only 12.9 % of CVE records carry package
coordinates, so OSV.dev is the package source of the MVP for npm, NuGet, Packagist, PyPI, Maven and Go. The plan
fixes the access path (per-ecosystem `modified_id.csv` and archive, never the query API, which would send an
organisation's dependencies to a foreign service) and asks for the terms of OSV's data sources to be checked
before the adapter is built.

## Endpoints, measured on 18 Nov 2026

All from `https://osv-vulnerabilities.storage.googleapis.com/` (public Google Cloud Storage bucket). Every object
sends `ETag`, `Last-Modified` and `Cache-Control: public, max-age=3600`; `If-None-Match` and `If-Modified-Since`
answer 304 (checked on a change list, an archive and a record); `Range` answers 206; a missing record answers 404.

| Ecosystem | `modified_id.csv` | `all.zip` | Records in the list |
|---|---|---|---|
| npm | 8.8 MB | 222 MB | 8,158 GHSA, 224,499 MAL, 2 others |
| NuGet | 88 KB | 2.8 MB | 1,232 GHSA, 782 MAL |
| Packagist | 366 KB | 11.2 MB | 6,612 GHSA, 849 DRUPAL, 1 MAL |
| PyPI | 1.2 MB | 38.1 MB | 6,896 GHSA, 8,011 PYSEC, 12,077 MAL, 12 OSV |
| Maven | 351 KB | 10.5 MB | 7,256 GHSA, 2 MAL |
| Go | 452 KB | 12.9 MB | 5,118 GHSA, 4,820 GO, 20 MAL, 1 OSV |

- `{ecosystem}/modified_id.csv`: `text/csv`, one line `timestamp,id` per record, newest first, timestamps with up
  to nine fractional digits; records changed together share a timestamp (a GHSA and its GO twin).
- `{ecosystem}/{id}.json`: `application/json`, one record (about 2–20 KB) as OSV.dev serves it, with `purl`,
  enumerated `versions` and computed aliases.
- `{ecosystem}/all.zip`: `application/zip`, one `{id}.json` per record at the root.
- `https://api.osv.dev/v1/vulns/{id}`: 200 `application/json` without validators; 404 with a JSON error for an
  unknown id; `HEAD` answers 405. Used only for a single record by id, which reveals nothing but that id.

## Terms of the data sources

OSV.dev publishes records under the licence of their source (`docs/data.md` of google/osv.dev). The sources that
feed the six ecosystems, with the prefix of their ids:

| Source | Ids | Licence | Checked at |
|---|---|---|---|
| GitHub Advisory Database | GHSA | CC BY 4.0 | `LICENSE.md` of github/advisory-database |
| PyPA Advisory Database | PYSEC | CC BY 4.0 | `LICENSE` of pypa/advisory-database |
| Go Vulnerability Database | GO | CC BY 4.0 | README of golang/vulndb («Database entries are distributed under the terms of the CC-BY-4.0 license») |
| Drupal Advisory Database | DRUPAL | MIT | `LICENSE` of DrupalSecurityTeam/drupal-advisory-database |
| OSS-Fuzz | OSV | CC BY 4.0 | `LICENSE` of google/oss-fuzz-vulns |
| Global Security Database | GSD (1 npm record) | CC0 1.0 | OSV.dev data sources |
| Erlang Ecosystem Foundation CNA | EEF (1 npm record) | CC BY 4.0 | OSV.dev data sources |
| OpenSSF Malicious Packages | MAL | Apache 2.0 | `LICENSE` of ossf/malicious-packages (not fetched, see below) |

Every licence permits what Ardea does: store, transform, show to the instance's users and keep the raw record.
CC BY 4.0 asks for attribution, a link to the licence and a note that the material was changed; MIT asks to keep
its notice. The source's `Attribution` names all of them with the CC BY link and «normalised by Ardea»; terms mode
is `Full`. Related identifiers of other databases (e.g. Chainguard `CGA-`, CC BY-NC-ND) are kept as identifiers
only; no data of those databases is fetched.

## Decision

**One source, one position per ecosystem.** Key `osv`, operator «Open Source Vulnerabilities (OSV), Google»,
`recognized`, every hour (the bucket's `max-age`). The cursor holds the start of the backfill window and per
ecosystem the time of the newest stored change and the list's validators. An ecosystem without a position is
filled from its archive; so an aborted first run and an ecosystem added later need nothing special.

**Incremental.** The change list is fetched conditionally and read only down to the position, so a run costs the
new lines, not 8.8 MB. Changed records are fetched oldest first from the bucket; a checkpoint follows every 200
records, but only between distinct timestamps, so a position never splits records changed at the same instant.
Timestamps are cut to the seven fractional digits of `DateTimeOffset`.

**Archive.** On the first start and when more than 2,000 changes are pending in one ecosystem (a long pause, or a
re-import at OSV.dev like the one of 10 Sep 2026 that touched most records), the ecosystem's `all.zip` is written
to the work volume and read entry by entry; records changed at or after the window start (or the old position)
are kept. The position then stands at the newest change in the archive. An entry is skipped when its header
declares more than 4 MB (the largest record measured is 177 KB, in NuGet), and the read itself stops at 4 MB;
an entry whose header claims less than it holds is cut off at the claimed size when unpacked, is then no valid
record and ends up in quarantine.

**Change log.** For the completeness check (task 1.11) the source implements `IChangeLogSource`: each
ecosystem's change list read down to the requested time, without `MAL-` records. Missing or stale records are
then fetched singly through the API by id.

**Malicious packages are skipped.** `MAL-` records (OpenSSF) report malicious package versions, not
vulnerabilities, and make up 96 % of npm's list and 45 % of PyPI's. They are filtered by id before any request.
Whether Ardea should warn about malicious packages in a profile is a product question for a later wave.

**Identifiers.** The record's id and `aliases` are aliases; CVE and GHSA keep their schemes, every other id of the
OSV namespace (`PYSEC-`, `GO-`, `DRUPAL-`, `BIT-`, …) gets the scheme `OSV`, which ranks after GHSA when the core
picks the primary key (6.2). `related` and `upstream` are related identifiers.

**Versions.** One product per `affected` entry, with the record's `purl` or one built from ecosystem and name (the
GitHub Advisory Database writes none). `ECOSYSTEM` and `SEMVER` ranges are compared with the ecosystem's scheme
(`semver`, `nuget`, `packagist`, `pep440`, `maven`, `go`; Go writes SEMVER without the `v` its scheme also reads);
other ecosystems keep their name, which no scheme answers, so they give «unknown» rather than a numeric guess.
Events pair in record order: `introduced` opens, `fixed` (exclusive) or `last_affected` (inclusive) closes,
`introduced: 0` has no lower bound, `limit` is ignored. `GIT` ranges are kept under scheme `git`. The `versions`
list repeats the ranges and runs to hundreds of entries, so it becomes exact versions only where an entry has no
comparable range. OSV names no default status; it stays empty («unknown»), as in the recorded version checks.

**Severity.** OSV carries CVSS vectors only. They are kept with the record's database as assigner and no score;
computing scores from vectors is left to one place in the core for all sources.

## Consequences

- The recorded version checks (60 cases) pass on the adapter's output, and a recorded GHSA record that gains its
  CVE id merges into the CVE entry with a redirect.
- Open for the core: a withdrawn OSV record is `rejected` in its claim, but the projection only lets the CVE
  Program reject an entry, so a withdrawn GHSA-only entry stays `published`, and a withdrawn advisory that still
  names a CVE contributes its affected ranges to the CVE entry. Severity scores for OSV-only entries need a CVSS
  calculator. The original publisher of a record (GitHub, PyPA, Go) has no field in the observation; it is
  visible only through the id and the metric's assigner.
