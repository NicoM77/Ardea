# Add a source

A source is an adapter in its own assembly `Ardea.Intel.Sources.<Name>` under `src/Sources/` that references only
the SDK `Ardea.Intel.Sources.Abstractions` (Apache 2.0) and, if useful, `Ardea.Intel.Sources.Formats`. The host
loads every `Ardea.Intel.Sources.*` assembly next to the program and finds its `ISourceProvider` or
`ISignalProvider` by type; the core needs no change and nothing registers the class. Two things make the assembly
part of the image: a project reference from `src/Hosts/Ardea.Host/Ardea.Host.csproj` (so that it is published next
to the program) and, for the tests, `InternalsVisibleTo` for `Ardea.Sources.Tests`. A provider needs a public
parameterless constructor for `ardea doctor` to list its hosts. Master plan chapter 5 is the reference; a licence
on the allow-list applies to every package the adapter adds ([`CONTRIBUTING.md`](../../CONTRIBUTING.md)).

1. **Check the terms first.** Store, transform, show: note what the source allows in `Terms` and
   `Attribution`. An adapter that may keep metadata only says so and keeps only that.
2. **Describe it** with a `SourceDescriptor`: key, name, kind (`Record` or `Signal`), operator and operator kind
   (authority, mandated, recognised, vendor, community), interval, **allowed hosts** (the only hosts its HTTP
   client can reach, also after redirects), request rate and size limits.
3. **Fetch** in `ISourceProvider.FetchAsync`: yield `RawRecord`s and, after each consistent stretch, a
   `Checkpoint` with the cursor; the core stores records and cursor together, so an aborted run resumes there.
   Use `context.GetIfChangedAsync` for conditional requests and `SyncMode` for incremental, backfill and
   single runs.
4. **Normalise** in `Normalize`: pure and deterministic, one `Observation` per record, or quarantine with a
   reason. Never guess missing information.
5. Optional: implement `IChangeLogSource` if the source publishes a change log; the daily completeness check
   then finds and fetches what a run missed. Signal sources implement `ISignalProvider` instead.
6. **Test** with recorded responses (keep fixtures small, note their origin in a README next to them):
   reader snapshots, a fetch test with a replaying handler, and the behaviour on a changed or broken answer.
   The adapter must not reach the network in tests.

7. **Try it.** Start the worker with the adapter in the image or in `dotnet run`: the source appears on the
   administration's *Sources* page with its health, last success, next run, records and quarantine, and can be
   paused or run now. `ardea doctor` checks that its allowed hosts can be reached. A source that fails backs off up to
   six hours and, after six hours without success, the system warns about it ([Operation](operations.md#warnings-and-figures)).
   `ardea intel renormalize --source <key>` reads its stored records again after a parser change (raise
   `ParserVersion`), `ardea intel import --full --source <key>` loads its whole inventory.

The existing adapters (`CveProgram`, `Osv`, `Ncsc`, `Signals`) show each step.
