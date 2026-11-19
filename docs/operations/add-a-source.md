# Add a source

A source is an adapter in its own assembly `Ardea.Intel.Sources.<Name>` that references only the SDK
`Ardea.Intel.Sources.Abstractions` (Apache 2.0) and, if useful, `Ardea.Intel.Sources.Formats`. The host loads
every such assembly in the image; nothing else needs registering. Master plan chapter 5 is the reference.

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

The existing adapters (`CveProgram`, `Osv`, `Ncsc`, `Signals`) show each step.
