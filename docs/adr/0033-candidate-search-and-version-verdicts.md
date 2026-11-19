# ADR 033: Candidate search, version verdicts and the assessment job chain

- Status: accepted
- Date: 2026-11-19
- Refines: ADR 010 (matching on structured fields only), ADR 011 (level per finding), ADR 029 (system functions)
- Master plan: 3.3, 6.6, 7.2, 7.3, 9.2, tasks 3.2 and 3.3

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

## Consequences

- A forward search costs one index lookup per key form in `component_key` and one per alert; the plans are
  asserted in `Ardea.Assessment.Tests.QueryPlanTests`, as are the three key lookups of the reverse search on
  `entry_affected`.
- Unknown WordPress plugins (no catalogue entry with `platform_key`) give no `ecosystem` hit yet: the
  affected rows carry no platform. The catalogue growing itself (master plan 6.9) narrows this gap.
- Merging two entries (master plan 6.2 rule 4) currently closes the findings of the merged entry with
  reason `merged`; the winner gets its own findings through its own change. Moving history across is open.
