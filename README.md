# Ardea

Ardea is Latin for heron: a bird that stands still for a long time and strikes only when it matters.

A self-hosted, open-source early-warning system for security vulnerabilities: it collects advisories and CVE records from trusted sources, merges them into one entry per vulnerability, and tells each organization which ones affect its projects, how urgent they are, and what to do.

## Status: planning

There is no production code yet. Implementation starts only after the owner has approved the master plan.

| What | Where |
|---|---|
| Master plan (German) | [`docs/masterplan/`](docs/masterplan/README.md) |
| Decision register | [`docs/adr/`](docs/adr/README.md) |
| Product record and design system | [`PRODUCT.md`](PRODUCT.md), [`DESIGN.md`](DESIGN.md) |
| UI mockups (static HTML, open `overview.html` in a browser) | [`docs/design/mockups/`](docs/design/mockups/) |
| Ecosystem research with sources, 18 Nov 2026 | [`docs/research/`](docs/research/) |
| Throwaway data spike | [`prototypes/`](prototypes/README.md) |

## Planned stack

ASP.NET Core on .NET 10, Angular 22, PostgreSQL 18, one container image with three roles (API, worker, migration), Docker Compose. AI assistance is optional and provider-neutral, including locally hosted models.

## Licence

Not decided yet. See chapter 13.9 of the master plan.
