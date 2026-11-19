# ADR 030: Catalogue format and master data loaded by migrate

- Status: accepted
- Date: 2026-11-18
- Refines: ADR 022 (catalogue as its own module)
- Master plan: 4.5, 4.11, 6.5, 6.6, 6.9, task 1.1

## Context

The curated catalogue (about 300 products after task 2.4) lives in the repository, is reviewed in pull
requests and ships with the image. `migrate` loads it into `catalog.product` after the migrations
(master plan 4.11). From task 4.9 on, the instance also learns entries (6.9) that the curated files must
neither change nor remove. Sources and the rule set will follow the same path.

## Decision

**Format.** One JSON file per vendor in `data/catalog/` (`microsoft.json`, `redhat.json`, …), each with the
vendor's name once and a list of its products with the fields of `catalog.product`. Comments and trailing
commas are allowed, unknown fields are errors, `data/catalog.schema.json` describes the format for editors.

- JSON instead of YAML: `System.Text.Json` reads it, so the production image needs no parser library;
  comments cover what YAML would add for curators; other tools (`jq`, Python) read it as is.
- Per vendor instead of per product: the vendor and its spellings are written once; a vendor's products
  (.NET and ASP.NET Core, RHEL and its packages) are reviewed together; 300 products stay around 120 files
  instead of 300. Products whose vendor changed hands stay in the file of the current vendor.

**Loading.** `IModuleDatabase.SeedAsync` is called by `DatabaseMigrator` for every schema after all
migrations, on every `migrate`. The files come from `ARDEA_DATA_DIR`, by default `data/` next to the program
(`/app/data` in the image) or the repository's `data/` in a source checkout. The catalogue seed validates
every file first (unique keys, spellings, CPE pairs, packages and images; known version schemes; existing
platform and related keys) and stops `migrate` on any error, so an empty or broken folder never wipes the
catalogue. It then upserts curated rows by key in one transaction and removes curated rows whose key left
the files. Learned rows are never changed, with one exception: a curated file that takes over a learned
key promotes that row to curated, which is how learned entries return to the project (6.9).

**Lookups** read the entries in effect (every curated one, learned ones only while `active`) into memory
for five minutes; curated entries win over learned ones on every shared spelling, CPE pair, package or image.

**Search** for the input help (task 2.4) runs over the same in-memory entries, without a search library:
the typed text is compared without case, accents, spaces and punctuation, and with and without trailing
version numbers. Tiers from strong to weak: equal to a key, name or alias, then to a CPE product, package or
image; whole words of a name; a name that starts with the text; whole words including vendor, CPE pair,
package or image; word prefixes; contained text; finally a bounded edit distance (one typo up to seven
characters, two beyond). Within a tier the closer length wins, then curated before learned, then the name.
Fifty inputs fixed in `CatalogSearchTests`, spelling variants and typos included, must find the right
product first (phase 2 acceptance); a curator who adds a product checks that its usual spellings are aliases.

## Alternatives

- YAML with YamlDotNet: friendlier to write, but a dependency in the image for a few hundred records.
- One file per product: no merge conflicts at all, but vendor spellings repeated in every file.
- Loading through an EF Core migration with `HasData`: every catalogue change would need a migration, and
  learned rows in the same table would conflict with EF's data seeding.

## Consequences

- Renaming a key removes the old row; components that refer to it lose their product. Keys are stable;
  a rename needs a later migration step for references, not a file edit alone.
- `migrate` fails on an invalid catalogue; the same validation runs as a unit test on every pull request.
