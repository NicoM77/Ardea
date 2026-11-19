# ADR 033: Candidate search, version verdicts and the assessment job chain

- Status: accepted
- Date: 2026-11-19
- Refines: ADR 010 (matching on structured fields only), ADR 011 (level per finding), ADR 029 (system functions)
- Master plan: 3.3, 6.6, 7.2, 7.3, 7.9, 9.2, tasks 3.2, 3.3 and 3.7

## Context

Phase 3 connects the consolidated entries (Intel) with the profiles (Assets). The plan fixes the matching
table of 7.2, the three version outcomes of 6.6 and the job chain `MatchEntry`, `MatchProject`, `AssessAlert`
of 3.3. It leaves open how the Assessment module reaches the data of two other modules, how several sources'
statements about one product combine into one verdict, which version scheme compares, and where the reverse
search stops looking back.

## Decision

1. **Read contracts, no tables of other modules.** Intel offers `IEntryQueries` (an entry with its affected
   products; the entries affecting a set of keys), Assets offers `IProfileQueries` (components under keys, a
   project with its components and keys, components by id), Catalog gains `PluginsOfAsync`. Assessment
   references only these Contracts; the architecture tests keep it so.
2. **Two system functions, ids only** (master plan 9.2, ADR 029):
   `assets.organizations_with_component_keys(types, values)` answers «which organisations carry these keys»
   (archived projects excluded), `assess.organizations_with_alert(entry)` answers «which organisations have an
   alert for this entry». `MatchEntry` runs without organisation, asks both and queues one
   `AssessAlert(entry)` per organisation. Which projects and components are hit is read only inside the
   organisation's job, through the tenant filter and row-level security.
3. **Match strength comes from the key, the match type from the hit.** A component hit under one of its
   strong keys is a strong candidate: `package` (purl), `cpe` or `product`, preferred in that order. Otherwise a
   weak key makes it `variant` (a related catalogue product) or `name` (a free entry without vendor). A
   platform whose plugin the entry names is a weak `ecosystem` candidate, unless the project already has a
   strong hit for the entry. Weak candidates are stored as level-1 findings with status `not_affected`,
   reason `weak_match`, without version check.
4. **One verdict from several sources.** The rows of `entry_affected` are grouped by source and product and
   each group is checked as one record with its default status (`VersionCheck.Evaluate`, master plan 6.6). The
   finding is *in range* when any group says so; *unknown* when a group cannot decide for a reason other than
   «names no comparable range»; *out of range* when at least one group says so and the rest name nothing
   comparable; *unknown* otherwise. A source that names no version at all does not veto a source that does;
   any source that places the version inside an affected range wins, because a missed finding is worse than
   a superfluous one (master plan 7.9).
5. **The scheme.** A package hit compares in its ecosystem's scheme. Otherwise the record's own version type
   when it names a specific one (not `custom`), then the component's scheme (its own or the catalogue's), then
   the numeric fallback; the first that reads the installed version is used.
6. **Distribution packages** are *unknown* with reason `distribution_package` before any range is looked at
   (master plan 7.3); the ranges are still kept as evidence.
7. **Fix version.** A finding in range stores the fix of the affected range that contains the installed
   version (the highest when sources differ): the target of its action (master plan 7.4). All fixes the
   sources name stay in the evidence for an unknown installed version.
8. **Lookback.** The reverse search looks at vulnerabilities changed in the last 180 days, the same window
   as the first load of the sources (master plan 4.10), plus every entry the project already has findings for.
9. **Closing without a person.** A finding the system opened closes when its candidate disappears: the entry
   was withdrawn or merged (`not_affected`), the component left the profile or its project was archived
   (`remediated`), the key no longer matches (`not_affected`, `no_longer_matched`), or the installed version
   left the range because the profile's version changed (`remediated`, `fixed_by_update`, master plan 7.4).
   A status a person set is never overwritten by this.
10. **Serialised per alert.** `AssessAlert` and the reverse search both write findings of the same alert;
    each takes a transaction-level advisory lock on organisation and entry first, so they never interleave.

11. **Reading the statements (calibration against the reference set, task 3.7).** The reference set of
    master plan 7.9 showed where the version check misread real records; an independent review of the
    result refined them. The guiding rule: never a wrong *out of range*; where a statement allows two
    readings, the one that cannot dismiss a vulnerable version is taken, and *unknown* is the safe side.
    The rules, in the order they act:
    - *Normalisation, once, at correlation.* The source readers keep bounds as the record writes them;
      `Projection` applies `VersionRanges.Normalize` to every source's ranges before they are stored in
      `entry_affected`. It splits ranges in one field (Oracle's `8.4.0-8.4.13`), drops open lower bounds
      (`0`, `*`, `unspecified`) and upper bounds that are a wildcard but no placeholder (`*`, `log4j-core*`),
      and resolves placeholders: `lessThanOrEqual 6.12.*` ends after the 6.12 line (before `6.13`).
      `lessThan 6.12.*` means either «before the 6.12 line» or «to the end of it»: with a lower bound in the
      line (`6.12.0`, `6.12.116`, `2.0` to before `2.x`) only the second reading is not empty, so the range
      ends after the line; below the line or without a lower bound the wider reading is taken for an affected
      range and the narrower one for an unaffected range; a lower bound without leading numbers keeps the
      placeholder, and the range is unreadable.
    - *Statements that name no version.* An exact «version» that is an open bound alone (`0`, `*`, `n/a`,
      `unspecified`) names no version: VulnCheck writes `{"version": "0", "status": "affected"}` for products
      whose versions it does not know. Such a range is skipped like a commit range (below); with nothing else
      in the statement it is *unknown, no comparable range*. Read as the version 0 it had put every real
      version «above all ranges».
    - *A lower bound equal to the fix*, compared as versions in the statement's scheme, makes an empty range.
      It is stored as written and never serves as an explicit lower bound. Below its fix the check reads it as
      «before the fix» (*in range*, `before_empty_range`): Google writes «Chrome prior to 157.0.8103.50» as
      `157.0.8103.50` to before `157.0.8103.50`. Microsoft writes a line that ships with the fix the same way
      («Visual Studio 2026 version 18.12», `18.12.0` to before `18.12.0`), so such a statement does not decide
      while another statement of the entry names the installed version's own line and places it out of range
      (VS 18.11.9 against the 18.11 statement fixed in 18.11.4); it stays in the evidence. An explicit
      unaffected range wins over the reading.
    - *Git.* A range of type `git` is skipped only when a bound is a commit hash (7 to 40 hex digits) or it
      names no bound; publishers also tag plain version numbers `git` (Symfony). When only skipped ranges are
      left, an unaffected or missing default status is not applied, because the version may lie inside one of
      them: *unknown, no comparable range*. An affected default still holds (*in range*), unless a skipped
      range is itself unaffected. As in decision 4, an unknown statement of this kind does not veto another one.
    - *Unreadable lower bound.* A version at or past a readable fix (or above a readable last affected
      version) is above the range whatever the lower bound says (`NOT_YET_AVAILABLE` to before `11.0.0`).
    - *Scheme.* Of the candidate schemes (decision 5), the first that reads the installed version **and every
      bound** of the statement is used; only if none does, the first that reads the installed version. A
      record that types some entries `semver` and leaves four-part versions untyped (Mattermost) is compared
      in the numeric fallback instead of being unreadable.
    - *Fix named in the text.* Intel reads the versions that the description and the solutions name after a
      fix word («fixed in», «upgrade to [product] version», «update [product] to», «patched in», «resolved
      in», «before», «prior to», «earlier than») into `security_entry.fix_versions_in_text`
      (`EntryFacts.FixVersionsInText`, part of the material fingerprint). When the installed version lies
      above every affected range and rows 4 or 5 of master plan 6.6 would say *out of range*, a fix named in
      the text above the installed version makes it *unknown* (`fix_named_in_text_above_installed`) instead,
      unless the ranges or the text name a fix of the installed version's own line at or below it. The line is
      read by `VersionLines` from the leading numbers, brought to at least three parts with trailing zeros
      beyond them dropped, then without the last part: 6.3 for 6.3.4, 16.4 for 16.4 and 16.4.0 alike: «upgrade to 6.4.0, 6.3.3 or 5.19.12» has fixed 6.3.4 with 6.3.3, and a range fixed at
      2.45.5 fixes 2.45.6 whatever «before 2.46.1» in the text says. «through X» does not count: it names the
      last affected version, and in descriptions listing several products it often belongs to another one.
      The rule never turns a verdict into *in range* and never touches a version inside an unaffected range
      or between ranges; it only keeps a conflict between ranges and prose from becoming «not affected».
      Prose is read in Intel, once, not in Assessment; the check compares the extracted versions in the
      statement's scheme. This keeps to ADR 010: the text never creates a hit, it only keeps a hit made on
      structured fields from being dismissed as out of range.

## Consequences

- A forward search costs one index lookup per key form in `component_key` and one per alert; the plans are
  asserted in `Ardea.Assessment.Tests.QueryPlanTests`, as are the three key lookups of the reverse search on
  `entry_affected`.
- Unknown WordPress plugins (no catalogue entry with `platform_key`) give no `ecosystem` hit yet: the
  affected rows carry no platform. The catalogue growing itself (master plan 6.9) narrows this gap.
- Merging two entries (master plan 6.2 rule 4) currently closes the findings of the merged entry with
  reason `merged`; the winner gets its own findings through its own change. Moving history across is open.
- Decision 11 changes what is stored (`entry_affected` bounds, `security_entry.fix_versions_in_text`; CVE
  parser version 2). Recomputing entries (`ardea intel recompute`) is not enough: the observations read by
  parser version 1 have already lost their wildcard bounds. At worker start, therefore, the source registrar
  sets every record that an older parser version read back to *pending* and queues its normalisation; the
  observations that come out different are correlated again, and their changed material fingerprints
  reassess the findings. A parser change thus applies to what is stored without fetching it again.
- Fix versions in the text belong to the entry, not to one affected product, so a description that names a
  later fix of another product can keep a version «unknown». The line rule and the restriction to «above
  every range» keep this rare in the reference set (no unfounded pair); the price is a superfluous unknown
  finding, never a missed one.
