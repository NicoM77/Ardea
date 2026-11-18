---
version: 1
slug: "docs-design-mockups-bulletin-html"
primary_target: "docs/design/mockups/bulletin.html"
related_targets: ["docs/design/mockups/alerts.html","docs/design/mockups/alert.html","docs/design/mockups/design-system.html"]
---

# Core flow mockups: bulletin, alert list, alert detail

## Scope

Planning-phase mockups for the core flow, built as static HTML under `docs/design/mockups/`: the bulletin (overview), the alert list, the alert detail with its actions, and a design-system page. Each page is responsive and is reviewed at 1440 and 390 px. Visitor mode: Operate. Setup wizard, project profile, notification rules and source status are out of scope until the owner approves this direction.

## Audience and job

The responsible person scans the bulletin at a desk and triages across projects. A developer lands on one alert from a notification, often on a phone, and needs the answer before the detail. Both must be able to say within five seconds what happened, whether they are affected, and what to do.

## Content and constraints

Real CVE records fetched on 2026-11-18 serve as demonstration data; the organization, people, project names and installed versions are sample data. English copy first, German later, so no layout may depend on short English words. Light theme only. WCAG 2.2 AA. Priority is never carried by colour alone.

## Direction contract

THESIS: Risk is reported the way a natural-hazard bulletin reports it: one numbered danger level, the area it applies to, and the recommended action. The surface refuses the security-dashboard default of KPI tiles, a severity donut and a feed of pill badges.

OWN-WORLD: White ground, an anthracite header band, and square numeral tiles on a five-step ramp that darkens from pale green through yellow, orange and red to oxblood. Black-weight numerals in a humanist sans, a three-pixel ink rule under section heads, hairline table rules. No rounded cards, no shadows, no gradients. Blue is reserved for links and focus; the primary action is solid ink.

STORY: The visitor reads today's highest level and where it applies, trusts it because every level states its reasons, its unknowns and its sources, and acts by taking the alert into review or closing it with an outcome.

FIRST VIEWPORT: Header band with wordmark, primary navigation, organization switcher and user. Left column, 300 px: the danger ladder, five steps from 5 to 1, each a numeral tile with level name and count. Main column: the bulletin date with a validity line over an ink rule; the lead block with a 132 px tile showing the highest level, the alert headline, the affected area and the recommended action side by side, and the primary action "Open alert" directly beneath; then the table "Level by project".

SIGNATURE: The danger ladder. It is filter, count and legend at once: selecting a step filters the alert list to that level. The same numeral tile marks priority everywhere, and a change of level is written as two tiles joined by an arrow. On a phone the ladder becomes a five-segment bar across the top. Motion is limited to state changes at 150 to 200 ms; nothing animates on load.

FORM: Natural-hazard bulletin (avalanche and weather warning bulletins, European five-level danger scale). Position 1 on the ordered list; chosen by the owner over the assigned direction. seed:15c9b436027217c8

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance

## Unresolved

- Product name: "CSH Alert" is a working title.
- Whether level 1 should read "Not relevant" or be hidden from the ladder by default.
- Setup wizard, notification rules, project profile and source status follow after approval.
