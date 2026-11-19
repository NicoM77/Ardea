# ADR 044: Notices per week and their thresholds

- Status: accepted
- Date: 2026-11-19
- Refines: ADR 033 (candidate search and version verdicts), ADR 036 (spike week as test data); master plan 17.2, 17.3
- Master plan: 17.2, 17.3, 17.8, 17.9, 14 (phase 5a acceptance), task 5a.1

## Context

Chapter 17 adds a coarse profile: the organisation's stack, a system project whose components carry no version or
only a line («PostgreSQL 17»). An entry that hits a product of the stack raises a **notice** («could affect you»,
yes or no), one per organisation and entry, if the stack entry's threshold is met: **only important** (proposed in
17.2 as high or critical severity, or exploited) or **everything**. A broad stack entry covers only the product
itself, not its ecosystem. 17.9 left open how many notices a typical stack gets per week, and asked to set the
thresholds from a measurement before building; phase 5a is accepted when a stack of ten entries gets at most the
number of notices set here in the spike week.

## Measurement

**Method.** `NoticeSpike` (decision 8) runs every record of the spike week through Ardea's real pipeline, as the
reference set does (ADR 036): CVE Program parser, correlation into entries, forward search, candidates and version
check, with the curated catalogue. Data: the CVE Program's delta archives of 11 to 17 November 2026, newest state per
CVE: 6 452 records (6 432 published, 20 rejected). 3 545 were published in the week; 2 907 were published before and
changed in it, among them Microsoft's Patch Tuesday of 10 November (1 865 records were published that day; the
Microsoft ones were updated several times during the week). Each stack is one organisation with one project whose components carry
no version. A hit counts when its match type is `product`, `cpe` or `package` (the catalogue product's own keys) and
a given line does not rule it out (decision 5); a notice is one entry (CVE) per organisation. Severity is the
entry's CVSS base score as Intel chooses it; «exploited» is `exploitation = active` (SSVC «active» or a KEV entry in
a record's CISA-ADP container). Every threshold lets an exploited entry pass. The run takes about five minutes.

Four stacks of ten entries were built from 17.8, plus the spike's three example projects without versions.

**Stacks, distinct notices in the week by threshold** (the same threshold for every entry; *default* is decision 2
and 3):

| Stack | Everything | ≥ medium | ≥ high (17.2) | Critical | Exploited only | **Default** | of them new |
|---|--:|--:|--:|--:|--:|--:|--:|
| Web agency: .NET, Angular, Azure | 828 | 822 | 604 | 14 | 0 | **14** | 0 |
| Hoster: PHP, WordPress, TYPO3 on Linux | 52 | 21 | 14 | 4 | 2 | **4** | 1 |
| Enterprise IT: Windows, Exchange, FortiGate, VMware | 1 266 | 1 135 | 872 | 38 | 2 | **19** | 3 |
| SaaS: Java, React, Kubernetes | 128 | 90 | 66 | 3 | 0 | **3** | 0 |
| Spike A: .NET, Angular, Umbraco, Azure SQL (4) | 23 | 23 | 17 | 1 | 0 | **1** | 0 |
| Spike B: WordPress, PHP, MySQL (3) | 8 | 8 | 7 | 3 | 2 | **3** | 1 |
| Spike C: Kubernetes, Linux, Redis, PostgreSQL (4) | 87 | 54 | 41 | 1 | 0 | **1** | 0 |

**Per entry.** *All*: hits on the product itself after the line; *new*: of them published in the week; *unrated*:
without a CVSS score; *weak*: hits a broad entry does not cover (related products, plugins of a platform), not
counted; *line*: hits the line removed.

Web agency:

| Entry | All | new | ≥ high | Critical | Exploited | unrated | weak | line | Default |
|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|
| .NET / ASP.NET Core | 22 | 0 | 17 | 1 | 0 | 0 | 0 | 0 | 1 |
| Angular | 0 | 0 | 0 | 0 | 0 | 0 | 15 | 0 | 0 |
| Node.js 22 | 10 | 10 | 4 | 0 | 0 | 0 | 0 | 5 | 0 |
| Umbraco | 1 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| Microsoft SQL Server | 52 | 0 | 43 | 1 | 0 | 0 | 0 | 0 | 1 |
| PostgreSQL 17 | 47 | 47 | 34 | 0 | 0 | 0 | 0 | 5 | 0 |
| Redis | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| IIS | 0 | 0 | 0 | 0 | 0 | 0 | 696 | 0 | 0 |
| Windows Server | 696 | 0 | 506 | 12 | 0 | 0 | 0 | 0 | 12 |
| Microsoft Azure (not in the catalogue) | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |

Hoster:

| Entry | All | new | ≥ high | Critical | Exploited | unrated | weak | line | Default |
|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|
| PHP 8.3 | 2 | 0 | 2 | 2 | 2 | 0 | 0 | 0 | 2 |
| WordPress | 0 | 0 | 0 | 0 | 0 | 0 | 8 | 0 | 0 |
| TYPO3 | 10 | 10 | 5 | 1 | 0 | 0 | 2 | 0 | 1 |
| Shopware | 0 | 0 | 0 | 0 | 0 | 0 | 4 | 0 | 0 |
| MySQL 8.0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 6 | 0 |
| nginx | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| Apache HTTP Server | 3 | 3 | 2 | 0 | 0 | 0 | 0 | 0 | 0 |
| Ubuntu Linux | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| Linux kernel | 35 | 23 | 3 | 1 | 0 | 31 | 0 | 0 | 1 |
| Docker | 2 | 0 | 2 | 0 | 0 | 0 | 0 | 0 | 0 |

Enterprise IT:

| Entry | All | new | ≥ high | Critical | Exploited | unrated | weak | line | Default |
|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|
| Windows Server | 696 | 0 | 506 | 12 | 0 | 0 | 0 | 0 | 12 |
| Windows 11 | 677 | 0 | 510 | 11 | 0 | 0 | 58 | 0 | 11 |
| Microsoft Exchange Server | 0 | 0 | 0 | 0 | 0 | 0 | 696 | 0 | 0 |
| Microsoft SQL Server | 52 | 0 | 43 | 1 | 0 | 0 | 0 | 0 | 1 |
| Microsoft Office | 176 | 2 | 151 | 1 | 1 | 0 | 0 | 0 | 1 |
| Fortinet FortiGate | 11 | 7 | 8 | 3 | 0 | 0 | 0 | 0 | 3 |
| VMware vSphere (ESXi, vCenter) | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| Veeam Backup & Replication | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| Ubuntu Linux | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| Google Chrome | 292 | 286 | 133 | 20 | 1 | 117 | 0 | 0 | 1 (exploited only) |

SaaS:

| Entry | All | new | ≥ high | Critical | Exploited | unrated | weak | line | Default |
|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|
| Java 21 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| Spring | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| React | 0 | 0 | 0 | 0 | 0 | 0 | 15 | 0 | 0 |
| Node.js | 15 | 15 | 7 | 0 | 0 | 0 | 0 | 0 | 0 |
| PostgreSQL | 52 | 52 | 38 | 0 | 0 | 0 | 0 | 0 | 0 |
| MongoDB | 26 | 0 | 18 | 2 | 0 | 0 | 0 | 0 | 2 |
| RabbitMQ | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| Elasticsearch | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| Kubernetes | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| Linux kernel | 35 | 23 | 3 | 1 | 0 | 31 | 0 | 0 | 1 |

**What the numbers say.**

- **«High or critical» does not filter.** CNAs score most records 7.0 to 8.8: 73 % of the agency's hits and 69 % of
  the enterprise's pass «≥ high». Vendor waves dominate: the Patch Tuesday gave Windows Server 696 hits (506 at 7.0 or
  more), a PostgreSQL release 52 (38), a Chrome release 292 (133). «Critical or exploited» keeps 2 to 8 % of the hits.
- **Browsers are the one outlier left at «critical»:** Chrome's release brought 20 critical entries, one exploited.
- **The Linux kernel is not the problem in this week:** 35 kernel records (17.9 expected hundreds), 31 of them
  without a score, because the kernel's CNA does not score; 3 at «≥ high», 1 critical.
- **«Only the product itself» matters as much as the threshold:** through related products IIS and Exchange would each
  get the 696 Windows hits, Angular and React 15 Node.js hits, WordPress 8 plugin hits.
- **A line narrows little but correctly:** «MySQL 8.0» drops all 6 MySQL records (they name 8.4, 9.7 and 26.10),
  «Node.js 22» 5 of 15, «PostgreSQL 17» 5 of 52; records that list every supported branch stay.

## Decision

1. **Only the product itself.** A stack entry raises notices only for hits of match type `product`, `cpe` or
   `package` on the catalogue product it names (17.2). Weak hits (related products, plugins of a platform, free
   names) never raise a notice.
2. **«Only important» is critical or exploited.** It is the default of every stack entry and is met when the entry's
   CVSS base score (as Intel chooses it) is 9.0 or more, or the entry is actively exploited (KEV, SSVC «active»),
   or a national authority warns about it, or its EPSS class is high. This replaces 17.2's «high or critical». An
   unscored entry passes only through the other signals; a score or exploitation that arrives later is a material
   change, and the notice is raised then.
3. **Noisy products: exploited only.** The catalogue may mark a product whose vendor ships large releases on a fixed
   cycle and updates automatically; for such a product «only important» means actively exploited or warned about by
   an authority. Marked: the browsers (Google Chrome, and Microsoft Edge and Mozilla Firefox for the same reason). The
   marking is curated catalogue data, without a model. Windows, Windows Server, Office, SQL Server and the Linux
   kernel keep decision 2: «critical» leaves 12 of a Patch Tuesday's 696 and 1 of the kernel's 35.
4. **«Everything»** raises a notice for every hit of decision 1 within the line.
5. **A line narrows only what the source rules out.** An entry stays unless every affected range it states for the
   product lies outside the line; a range the line's numbers cannot place (commits, wildcards, a bare «0») and an
   entry without ranges stay.
6. **What counts.** A notice exists once per organisation and entry; it is raised when the entry first meets the
   threshold of one of the stack entries it hits, whether the record is new or changed. The spike sees every record
   of the week for the first time, so its count is the upper bound of a week with a vendor wave in it; in steady
   state a change to an entry that already has a notice raises none.
7. **Acceptance: at most 25 notices per week for a stack of ten entries.** Measured with the defaults: 19, 14, 4
   and 3; the highest (enterprise IT) owes 12 to the Patch Tuesday. The margin covers signals the spike week lacks
   (EPSS, authority warnings, KEV additions to older entries; see limitations). With 17.2's «high or critical» the
   same stacks would get 872, 604, 66 and 14.
8. **The harness stays.** `tests/Ardea.Assessment.Tests/Reference/NoticeSpike.cs` is skipped unless `ARDEA_SPIKE_DIR`
   names the unpacked week; it never runs in CI (master plan 13.6). Run it with:

   ```
   ARDEA_SPIKE_DIR=<folder with 11 … 17, each holding the day's unpacked deltaCves> \
     dotnet test --project tests/Ardea.Assessment.Tests --filter-class "*NoticeSpike"
   ```

   It needs Docker (Testcontainers), takes about five minutes, writes `notice-spike-report.md` next to the test
   binaries (all tables above, and the notices with the default thresholds) and fails when a ten-entry stack gets
   more than 25 notices. The line rule of decision 5 is checked in CI by `LineNarrowingTests`, without data.

## Alternatives

- **17.2's «high or critical»:** 604 to 872 notices a week for stacks with Microsoft products, 66 for a stack without.
  Rejected: nobody answers that many yes-or-no questions.
- **«Exploited only» for every product:** 0 to 2 a week; it would have missed the week's critical FortiOS (9.6) and
  TYPO3 (9.4) entries. Too quiet for an early warning.
- **Per-product thresholds for Windows and the kernel:** not needed once «important» means critical.
- **One notice per stack entry and week («Windows Server: 696 entries»):** contradicts 17.3, where a notice is
  answered per entry and an «affects us» becomes a reminder for that entry. The Notices page may still group by stack
  entry.
- **Measuring with today's KEV and EPSS:** not reproducible for the week; left as a limitation.

## Consequences

- Tasks 5a.2 and 5a.3 store `important` or `everything` per stack entry and evaluate «important» as in decisions 2
  and 3; the catalogue format gains the marking of decision 3 (a schema change in 5a.3, not here). The threshold is
  checked again on every material change of an entry.
- Once 5a.3 writes `assess.notice`, the harness counts notices from it instead of from findings; the bound of 25
  stays the acceptance.
- Many hits stay below the threshold: the week's 47 PostgreSQL 17 entries (mostly 8.8, code execution by a database
  user) raise no notice. That is the price of a coarse stack; projects with versions keep the precise path
  (17.4), and an entry set to «everything» shows them all. Showing per stack entry how many hits stayed below the
  threshold is a suggestion for 5a.5, not decided here.
- Catalogue and wizard (5a.4): Microsoft Azure has no catalogue product (Microsoft names each Azure service), so an
  «Azure» entry gets nothing; IIS and Exchange are named in Windows records, so the wizard should suggest Windows
  Server with them; «Ubuntu» covers Ubuntu itself, not the kernel or packages it ships, so the wizard should suggest
  the Linux kernel with a distribution.
- 17.9's open point is answered here; 17.2's proposed default is replaced by decision 2.

## Limitations

- One week, the CVE Program only: no OSV or GitHub advisories (package hits), no NCSC warnings, no KEV or EPSS feed.
  Exploitation comes only from CISA-ADP containers in the records; KEV additions to older entries in that week are
  missing. Decision 2's EPSS and authority signals are therefore unmeasured.
- The week has its own outliers: a Patch Tuesday wave the day before, a PostgreSQL release with 52 entries, a Chrome
  release with 292. Another week will differ; the harness can run on any week's archives.
- The stacks are built from 17.8, not taken from customers.
- Notices are counted from the findings of the existing pipeline; `project.kind`, `version_precision` and
  `assess.notice` do not exist yet, and the line rule of decision 5 lives in the harness until 5a.3 builds it.
- Severity is the entry's one chosen CVSS score (the CNA's before an ADP's, the newest CVSS version first); a record
  with CVSS 3.1 and 4.0 scores can fall on either side of 9.0.
