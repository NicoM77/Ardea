# ADR 040: The catalogue that learns

- Status: accepted
- Date: 2026-11-19
- Refines: ADR 022 (catalogue as its own module), ADR 023 (read, call out, write), ADR 030 (catalogue format), ADR 037
  (assessment by a language model)
- Master plan: 4.5, 6.5, 6.8, 6.9, 7.8, 11.4, 14 (phase 4), tasks 4.9 and 4.10

## Context

Master plan 6.9 lets the catalogue grow in operation: unknown product keys of the sources are collected, mapped
without a model where rules can, and otherwise put to a language model; confident answers take effect, the rest goes
to the instance administrator. A learned entry may add hits but must never change a curated entry nor remove a hit
(phase 4 acceptance). Open were where the work lives between Intel and Catalogue, how «never remove a hit» holds when
an entry's key changes, which provider the catalogue may use, how a hit names the learned entry, and what the export
looks like.

## Decision

1. **Two layers in every lookup.** The catalogue in memory is two indexes: the curated entries alone, and curated with
   learned ones. Every lookup (by key, CPE pair, package, image, name; the resolution of an affected product) asks the
   curated index first and the full one only when it found nothing. So any spelling, CPE pair or package the curated
   catalogue knows gives the same answer whatever was learned; a fast test checks this for every spelling of the
   curated files against learned twins that claim all of them. `ProductMatch.Learned` and `ResolvedProduct.LearnedFrom`
   say when only a learned entry gave the answer.
2. **A learned key keeps its raw key.** `intel.entry_affected` gains `learned_from`: the raw key (`acme/widgetserver`) a
   learned entry resolved to its key (`widget-server`). The candidate search adds the raw key as a product key of the
   entry and the reverse search finds rows by it, so a free component that matched the raw key before still does; a
   free component whose name the catalogue knows only through a learned entry keeps its raw key besides. The
   fingerprints compare a row as before when it has no `learned_from`, so no existing entry looks changed.
3. **Who does what.** The catalogue owns learning: the tables, the job `GrowCatalog`, the checks, the decisions, the
   export, the measures and the endpoints. It never reads the entries: Intel implements `IUnknownProductFeed` (the
   unknown keys with their records, KEV listing and the statement the records make most often; titles per key) and
   `ICatalogChangeSubscriber` (merge the entries again that name a changed raw key, a withdrawn key, or a learned
   entry's CPE pair or package). Both interfaces are in `Ardea.Catalog.Contracts`, which Intel may reference.
4. **States.** `catalog.unknown_product` is one case per raw key: `open` (collected), `pending` (a proposal waits),
   `alias` or `new` (in effect), `unclear` (the model could not say, or its answers failed the check twice), `rejected`
   and `withdrawn` (never proposed again). `proposal` holds what was learned, by whom (`rule`, `model`, `admin`), with
   confidence, one sentence of reason, provider, model, prompt version and why it waits (`hold`: `confidence`,
   `suspicious_input`, `invalid_output`, `unclear`, `check`). A new product is a `catalog.product` row with origin
   `learned`, written only when it takes effect; withdrawn, it stays as `withdrawn` and keeps its key reserved.
   `catalog.learning_event` records collections, calls, proposals and decisions, counts only.
5. **Rules without a model** (step 2, also without any provider): a CPE pair or package an entry knows, or the same
   name but for trailing versions, the vendor's name in front, legal form and spelling: alias. An unknown CPE pair: a
   new entry with name and vendor of the record and the numeric scheme. Beyond the plan, an unknown package: a new
   entry of kind library with the ecosystem's scheme (npm → semver, PyPI → pep440, …); a package coordinate is as
   unambiguous as a CPE pair. Rule results take effect at once.
6. **The model** (steps 3 to 5) follows ADR 037: a versioned prompt file `Learning/Prompts/learn-1.md`, a data block
   with `<` and `>` escaped, `GetResponseAsync<LearnAnswer>` with the JSON schema, no tools, one retry after a refused
   answer, a time limit per call, a call without usage charged its estimate. The answer is `decision` (`alias`, `new`,
   `unclear`), `alias_of`, `new_product` (name, vendor, kind, version scheme, CPE pairs, related, platform),
   `confidence` and `reasoning`. The check refuses unknown or missing fields, values of the fixed lists not written
   exactly, a key that is not among the five candidates, a CPE pair not from the input, a related product or platform
   not among the candidates, a name with markup, an address or a pattern character, a name or vendor-less alias any
   entry already has, a key that exists (also withdrawn), and both or neither of `alias_of` and `new_product`. The key
   of a new product follows from its name (`widget-server`, else with the vendor in front); the record's own spelling
   becomes its alias under the record's vendor. Every decision is checked again against the catalogue of the moment
   it is written.
7. **Adoption.** In effect at once: rule results, and the model's checked answers with `confidence` high. Everything
   else waits on the review list. A source statement that reads like an instruction (markup, quotes or braces,
   control characters, more than 120 characters, «ignore … previous», «instructions», «you are», the answer's field
   names) is still mapped and asked about, but nothing of it takes effect without a person.
8. **Provider.** The catalogue sends public data of the sources only, so no organisation's data policy applies. The
   instance setting `catalog.learning_provider` (environment `ARDEA_AI__CATALOG_PROVIDER`, which locks it) names the
   provider: unset means the first local provider, `none` switches the model off, and a provider that is not local is
   used only when the administrator names it there. `migrate` and the start refuse a name that is not configured.
9. **Limits.** `ARDEA_AI__CATALOG_DAILY_CALLS` (200) calls per UTC day, retries included, counted from the `asked`
   events; a key is worked on from two records on, at once when one is in KEV, the most frequent first; a run asks
   about at most ten keys and queues its continuation while calls are left. A key whose call failed on the provider is
   asked again the next day. The job has one attempt and runs daily at 02:41 UTC, by `ardea catalog grow` and from
   the administration; the job's own unit of work stays unused, so no transaction is open during a call.
10. **Recomputation.** After a decision the catalogue tells `ICatalogChangeSubscriber`; Intel queues `CorrelateEntry`
    for the entries concerned with a delay of six minutes, longer than the five minutes a process keeps the catalogue
    in memory, so every process reads the new catalogue first. New or lost hits follow like any other entry change.
11. **The note on the hit.** A strong hit whose matched keys all come from learned entries only (or that matched a
    component recorded under a learned product) carries `evidence.learned` with the key and the raw keys. The web
    interface shows «via learned catalogue entry» and, for the instance administrator, links the entry on the review
    list, where it is withdrawn with one click. A closed finding keeps its evidence as history.
12. **Suggestions for free entries.** A component without catalogue key whose name the catalogue now knows gets
    `catalogMatch` (key, name, learned) in the project's component list; switching is a member's action through the
    existing component update. Nothing of an organisation flows into the catalogue.
13. **Export.** `GET /instance/catalog/learned/export` returns a zip with one `{vendor}.learned.json` per vendor in the
    format of `data/catalog/` with comments on origin, records, confidence and reasoning, and a `README.txt` with the
    learned spellings of existing entries, which belong into those entries' aliases. Dropped into `data/catalog/` next
    to the curated files, the files load and validate (test); a curated file that takes over a learned key promotes the
    row on the next `migrate` (ADR 030).
14. **Measures.** `GET /instance/catalog/learning`: the share of unknown product keys at the last collection, keys by
    outcome, calls of the day against the quota, and per week of the last eight learned, adopted, confirmed, rejected
    and withdrawn entries. The meter `Ardea.Catalog.Learning` counts calls, retries and keys by outcome.
15. **Reference set.** The reference run uses the curated catalogue only: its host never runs `GrowCatalog`, and the
    test asserts that no learned entry and no learned mapping exists when the levels are compared.

## Alternatives

- **Learning in Intel.** Intel has the data, but the catalogue owns spellings and keys; learned aliases in Intel would
  be a second catalogue.
- **Learned aliases inside curated rows.** Adding a spelling to a curated entry's `aliases` changes a curated row and
  is overwritten by the next `migrate`; the mapping lives on the raw key instead.
- **Keeping the raw key as the learned entry's key.** No row would change and no hit could be lost, but the entry
  would have an ugly key, and every other spelling of the same product would need its own entry.
- **Recomputing at once.** Another process may still hold the old catalogue for up to five minutes and would compute
  the entries unchanged; the delay is simpler than a cache version shared between processes.
- **A token quota.** The plan bounds the catalogue by calls; a call is short and its size varies little.

## Consequences

- A learned entry can still make a hit appear that is wrong; it is visible as such, and the administrator withdraws
  it with one click. The withdrawal rate per week is one of the measures.
- The first run after a 180-day first fill maps thousands of keys by rule, mostly unknown CPE pairs and packages, and
  queues the recomputation of their entries.
- The observation of the plan with a local model at 50 unknown keys is a manual step; the procedure is in
  `docs/operations/operations.md` («Catalogue that learns»). It was not run for this change.
