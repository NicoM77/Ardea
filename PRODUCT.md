# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Stack

Angular single-page app (decided by the owner), served by an ASP.NET Core / .NET 10 API with a separate .NET worker and PostgreSQL. A Capacitor wrapper for mobile is planned later; it wraps the same web UI, so the design language stays web. During the planning phase the UI exists only as isolated static HTML mockups under `docs/design/`; no production frontend code is written before the owner approves the master plan.

## Users

Two groups share the product, confirmed by the owner:

- **The responsible person** (IT or security lead, tech lead). Technically fluent, not necessarily a full-time security analyst. Triages regularly across all projects of the organization, decides what matters, assigns work, and tracks status.
- **Developers and DevOps engineers.** Arrive event-driven, through a notification or an assignment, often on a phone and outside their normal task. They need to know within seconds whether their project is affected and what to do, then leave.

Organizations range from small companies and agencies to larger organizations running several projects with different technology stacks.

## Product Purpose

A self-hosted, open-source early-warning system for security vulnerabilities. It continuously collects advisories and CVE records from trusted sources, merges reports about the same vulnerability into one consolidated entry, and assesses per organization and project whether that vulnerability actually affects the technology in use.

Success means a user can answer three questions within five seconds of opening an alert: what happened, are we affected, and what do we have to do.

## Positioning

The product does not forward more security news. It compares every vulnerability with the technology profile an organization has declared for each of its projects and surfaces only what plausibly affects it, with a priority that reflects exploitation and exposure rather than the CVSS score alone. Every AI-assisted assessment stays checkable against the sources it was derived from. The whole system runs on the organization's own infrastructure.

## Operating Context

- Organizations own projects; each project carries a technology profile (products, versions, exposure, criticality).
- Sources at the start: the Swiss National Cyber Security Centre (NCSC) and the CVE Program. The architecture targets 30 to 50 sources.
- An alert is the pairing of one consolidated vulnerability with one organization. It carries a priority, an assessment with reasoning and stated uncertainty, the affected projects, recommended actions, the underlying sources, and a change history.
- Priority levels: Critical, High, Medium, Low, Not relevant.
- An action bundles every open finding of one component in one project, for example "update PostgreSQL in project C". It is the unit people work on and are notified about; one action can cover many alerts.
- Working states, set on an action and its findings: Open, In review, Remediated, Risk accepted, Not affected.
- People are notified by email, web push, in-app notifications, and webhooks, according to rules each organization defines. An action re-alerts only when its risk or the required step changes materially.

## Capabilities and Constraints

- Interface language: English first; German follows as a translation. All copy must survive both languages.
- Fully responsive: desktop, tablet, and phone are all first-class.
- Multi-tenant: a user can belong to several organizations.
- The AI is a decision aid. Its conclusions must show their reasoning, their uncertainty, and their sources; a deterministic rule set computes the final priority.
- The system must stay useful without any AI provider configured.
- Planning phase only: mockups and prototypes are allowed, production implementation is not.
- Product name: Ardea, Latin for heron. Chosen by the owner on 2026-11-18; a trademark search is still due before the first public release.
- Undecided: licence, hosted offering, and any commercial tier. Do not imply any of them.

## Brand Commitments

The name is Ardea (Latin for heron). The logo is an outlined heron, head and neck in one line, with the wordmark in Inter Tight SemiBold; chosen by the owner on 2026-11-18. Files and rules: `docs/design/brand/` (open `index.html`). The wordmark face is for the logo and display lines only; the interface keeps the system font.

Binding constraints the owner stated in the brief:

- Light mode is the primary and, initially, the only theme.
- Calm, uncluttered surfaces; restrained neutral colours with deliberate accents.
- Priority and status are never conveyed by colour alone.
- Motion only where it carries function. No decorative effects, no overloaded dashboards, no complex interaction flows.
- The interface must read as serious, professional, and trustworthy; it is a security-critical tool.
- Guiding line: "Understand the risk. Know what matters. Act immediately."

Standing design preference, stated by the owner on 2026-11-18 after rejecting the first direction ("hazard bulletin"):

- Simple and intuitive, like a professional work application that follows Apple's design guidelines: familiar platform controls, the system font, no invented visual metaphor. It must not look AI-generated.
- Priority is shown as a word with its own symbol, never as a numeral.

## Evidence on Hand

- Real CVE records from the CVE Program (record format 5.2), fetched 2026-11-18, including CISA SSVC and KEV data embedded in them. They may be used as demonstration content.
- Measurements from the planning spike in `prototypes/relevance-funnel/`: one week of CVE data (6 432 published records) reduced to 173 structured candidates for three example projects.
- No customers, testimonials, usage numbers, benchmarks, or pricing exist. Sample organizations, people, and project names in mockups are fictional and must be recognisable as sample data.

## Product Principles

1. **Relevance before volume.** Showing fewer, better-founded alerts is the product; silence is a valid and trustworthy state.
2. **The answer first.** What happened, whether we are affected, and what to do come before any technical detail.
3. **Every judgement is checkable.** Assessments name their sources, their reasoning, and what is unknown.
4. **Urgency is earned.** The strongest visual signal is reserved for what demands action now.
5. **No lock-in.** Self-hosted, open source, usable without a proprietary cloud service.

## Accessibility & Inclusion

WCAG 2.2 AA. Full keyboard operability, clear contrast, understandable status indicators, and no information carried by colour alone.
