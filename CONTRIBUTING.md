# Contributing to Ardea

The rules below summarise chapter 13 of the [master plan](docs/masterplan/13-repository-und-richtlinien.md).

## Workflow

- One main branch, short-lived branches. Every change goes through a pull request with green checks.
  **Temporary exception (owner's decision of 19 November 2026, Actions minutes used up):** CI runs only on `main`,
  never on pull requests (task 7.13 turns it back on). A pull request is verified locally before it is merged: the
  test projects it touches and `dotnet format Ardea.slnx --verify-no-changes`; the pull request lists what passed.
  Changes to documentation alone run nothing.
- Commits follow [Conventional Commits](https://www.conventionalcommits.org/) (`feat:`, `fix:`, `chore:`, `docs:`, `test:`, `refactor:`, `ci:`).
- Decisions with lasting impact get an ADR in `docs/adr/`: context, decision, alternatives, consequences.
- Vertical slices: a feature is finished through every layer before the next one starts. Nothing is built in advance.

## Code

- `Nullable` and `TreatWarningsAsErrors` are on; formatting follows `.editorconfig` (`dotnet format`).
- Types of a module are `internal`. Only the module entry point (`*Module`) is public; whatever other modules
  may use lives in the module's `Contracts` assembly. The architecture tests enforce this.
- A module uses another module only through its `Contracts`. Allowed directions are in master plan 3.2.
- Domain logic does not depend on EF Core, HTTP or the clock; time comes from `TimeProvider`.
- Asynchronous all the way, with a `CancellationToken` passed into every I/O call.
- Logging through generated log methods with fixed event ids; never profile contents or secrets.
- No mediator, no object mapper: endpoints call handlers directly, mappings are written in code.

## Dependencies

A new dependency needs a reason in its pull request. Its licence must be on the allow-list in
`build/licenses/allowed-licenses.json` (MIT, Apache-2.0, BSD-2/3-Clause, ISC, PostgreSQL, MPL-2.0, OFL-1.1).
Excluded because they are commercial or impose conditions on operators: MediatR, AutoMapper, MassTransit 9+,
FluentAssertions 8+, ImageSharp, Duende IdentityServer, TickerQ, PrimeNG 22+, Verify 33+ (snapshot tests; build-time maintenance-fee check).

## Tests

Every pipeline runs in 15 to at most 25 minutes. Many fast unit tests, few tests against the real database,
a handful of end-to-end tests for the core flows. No long-running and no scheduled tests.

A test checks a rule or a defect that can be named, and its name says which. It sits at the lowest level where
the statement is possible. There is no coverage quota.

### Measuring coverage

Coverage shows where a rule may lack a test; it is not a goal and the pipeline does not measure it.
`build/coverage.sh` runs one test project under [coverlet](https://github.com/coverlet-coverage/coverlet)
(`coverlet.console` from the local tool manifest, MIT) and prints line and branch coverage per assembly:

```sh
build/coverage.sh Ardea.Catalog.Tests '[Ardea.Catalog]*' '[Ardea.Catalog.Contracts]*'
build/coverage.sh Ardea.Iam.Tests        # every Ardea assembly the tests load
```

The report for the uncovered lines is `artifacts/coverage/<TestProject>.cobertura.xml`, which IDEs and report
tools read. Migrations and generated code are left out. Tests against the database need Docker, as for `dotnet test`.
