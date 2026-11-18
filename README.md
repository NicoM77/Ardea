# Ardea

Ardea is Latin for heron: a bird that stands still for a long time and strikes only when it matters.

A self-hosted, open-source early-warning system for security vulnerabilities: it collects advisories and CVE records from trusted sources, merges them into one entry per vulnerability, and tells each organization which ones affect its projects, how urgent they are, and what to do.

## Status: phase 0 (foundation)

The master plan is approved and implementation has started. Progress follows the phases in
chapter 14 of the plan; nothing is usable yet.

## Development

Requirements: .NET SDK 10.0.4xx (pinned in `global.json`), Docker for integration tests, Node 24 for the web app.

```bash
dotnet build Ardea.slnx
dotnet test --solution Ardea.slnx
dotnet format Ardea.slnx --verify-no-changes
build/check-licenses.sh            # NuGet licences against the allow-list
python3 build/check-banned-packages.py
```

Web app (`web/`, Angular 22, Node 24): `npm ci`, then `npm start` for the dev server (proxies
`/api` to `http://localhost:5080`), `npm test`, `npm run lint`, `npm run build`. The component
library pages are at `/ui` in development builds. Details in [`web/README.md`](web/README.md).

The repository layout and the rules for code, dependencies and tests are in chapter 13 of the
master plan and in [`CONTRIBUTING.md`](CONTRIBUTING.md).

| What | Where |
|---|---|
| Master plan (German) | [`docs/masterplan/`](docs/masterplan/README.md) |
| Decision register | [`docs/adr/`](docs/adr/README.md) |
| Product record and design system | [`PRODUCT.md`](PRODUCT.md), [`DESIGN.md`](DESIGN.md) |
| UI mockups (static HTML, open `overview.html` in a browser) | [`docs/design/mockups/`](docs/design/mockups/) |
| Ecosystem research with sources, 18 Nov 2026 | [`docs/research/`](docs/research/) |
| Throwaway data spike | [`prototypes/`](prototypes/README.md) |
| Contribution rules | [`CONTRIBUTING.md`](CONTRIBUTING.md) |

## Planned stack

ASP.NET Core on .NET 10, Angular 22, PostgreSQL 18, one container image with three roles (API, worker, migration), Docker Compose. AI assistance is optional and provider-neutral, including locally hosted models.

## Licence

The application is licensed under the [GNU Affero General Public License v3.0](LICENSE). The provider SDK
(`src/Sources/Ardea.Intel.Sources.Abstractions`) is licensed under the
[Apache License 2.0](src/Sources/Ardea.Intel.Sources.Abstractions/LICENSE), so that third parties can write
source adapters under a licence of their choice (decision E2, master plan 13.9).
