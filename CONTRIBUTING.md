# Contributing to Ardea

The rules below summarise chapter 13 of the [master plan](docs/masterplan/13-repository-und-richtlinien.md).

## Workflow

- One main branch, short-lived branches. Every change goes through a pull request with green checks.
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
