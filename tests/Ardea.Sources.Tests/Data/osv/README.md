Real OSV records and change lists, fetched on 18 Nov 2026, unchanged. Files are named `{ecosystem}.{id}.json`
after the bucket folder they came from.

- `*.json` without suffix: from `https://osv-vulnerabilities.storage.googleapis.com/{ecosystem}/{id}.json` or the
  ecosystem's `all.zip` of that day, i.e. as OSV.dev serves them (with `purl`, enumerated `versions` and
  computed aliases). 33 of them are the records behind `tests/Ardea.Catalog.Tests/Recorded/osv-version-checks.json`.
- `Go.GHSA-pfrf-9r5f-73f5.ghsa-only.json`: the same advisory as first published on 8 Dec 2025, from the GitHub
  Advisory Database at commit `f346872f70185aab9cb6105a52a61a804145dddc` (no CVE id yet, no `purl`).
  CVE-2026-29067 was added on 9 Mar 2026 (commit `6046e4bb57cf3f956c83ec0dddb8fef504ac0fb2`); the current
  record is `Go.GHSA-pfrf-9r5f-73f5.json`. `tests/Ardea.Intel.Tests/Data/CVE-2026-29067.json` is its CVE record.
- `*.modified_id-head.csv`: the first lines of the ecosystem's `modified_id.csv` (newest first).

Picked for: a GHSA whose CVE id came later (ZITADEL), withdrawn records (`Maven.GHSA-22c5-cpvr-cfvq`,
`NuGet.GHSA-wrm2-c2h2-4w82`), Go `+incompatible` and pseudo-versions (`Go.GO-2026-6547`, ZITADEL), Maven
`.Final` and milestone versions (`Maven.GHSA-22c5-cpvr-cfvq`, `Maven.GHSA-23hv-mwm6-g8jf`), a PyPA record with a
`GIT` range beside the ecosystem range (`PyPI.PYSEC-2025-9`), an entry with only a `versions` list
(`Go.GHSA-3cw6-2j68-868p`), a Drupal record (`Packagist.DRUPAL-CORE-2026-003`) and one advisory across PyPI,
Maven and SwiftURL (`Maven.GHSA-xc7w-r669-48pf`).

Licences of the data: GitHub Advisory Database (GHSA), PyPA Advisory Database (PYSEC) and Go Vulnerability
Database (GO) under CC BY 4.0; Drupal Advisory Database (DRUPAL) under the MIT licence; served by OSV.dev.
