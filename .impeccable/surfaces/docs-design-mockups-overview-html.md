---
version: 1
slug: "docs-design-mockups-overview-html"
primary_target: "docs/design/mockups/overview.html"
related_targets: ["docs/design/mockups/actions.html","docs/design/mockups/action.html","docs/design/mockups/vulnerability.html","docs/design/mockups/design-system.html"]
---

# Core flow mockups: overview, actions, action, vulnerability

## Scope

Planning-phase mockups for the core flow as static HTML under `docs/design/mockups/`: the overview, the list of actions, one action, one vulnerability in the template variant without a language model, and a design-system page. Each page is responsive and is reviewed at 1440, 820 and 390 px. Visitor mode: Operate. Setup wizard, project profile, notification rules and source status follow after approval. This replaces the earlier "hazard bulletin" mockups, which the owner rejected on 18 Nov 2026 as not simple and not intuitive enough.

## Audience and job

The responsible person scans the overview at a desk and triages actions across projects. A developer lands on one action from a notification, often on a phone, and needs to know within seconds what happened, whether the project is affected and what to do, then leaves.

## Content and constraints

Real CVE records fetched on 2026-11-18 serve as demonstration data; organization, people, projects, installed versions and the national warning are sample data and say so. English copy first, German later. Light theme only. WCAG 2.2 AA. Priority is never carried by colour alone. Owner decisions of 18 Nov 2026: priority is a word plus a shaped symbol, no numerals and no danger ladder; the list shows actions (one component in one project), the vulnerability is the detail behind it.

## Direction contract

THESIS: A security tool that looks and behaves like a well-made system application: familiar controls, one clear next step per screen, and nothing on the page that does not help answer what happened, are we affected, what to do. It refuses both the dark security-dashboard costume and any invented visual metaphor.

OWN-WORLD: Apple platform conventions on the web. Light grey canvas, white grouped sections with 12px corners and hairline separators, a quiet grey sidebar with a selected row in light blue, the platform system font, one blue for actions, links and focus. Priority is the only other colour: red octagon Critical, orange triangle High, amber diamond Medium, grey circle with arrow Low, grey circle with dash Not relevant, each always with its visible word. Statuses are neutral icons with words. Segmented controls, pop-up buttons, inset lists with chevrons.

STORY: The visitor sees how many actions are open and which one comes first, opens it, reads the three answers, and takes it into review with one tap, with undo in place. Every priority can be traced to its factors and sources one level deeper.

FIRST VIEWPORT: Desktop: 248px sidebar (organization, navigation with counts, user) beside a canvas. Overview: large title and date, one sentence of status, a four-part priority bar that filters the list, then "Next up" as an inset list of the most urgent actions and "Recent changes" beside it. Action: priority, title, project and due date, primary button "Take into review", then the three answers. Phone: large title, content, bottom tab bar; on an action the tab bar becomes an action bar with the primary button, and the three answers sit above it.

FORM: User-pinned canon, Apple Human Interface Guidelines for macOS and iOS system apps (Settings, Mail, Reminders), played straight as a professional work application. The owner's words ("simple, intuitive, Apple design, a professional work application, not AI slop") override the roll seed:8631c750.

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance

## Unresolved

- Whether Low should get a colour of its own once real data shows how often it appears.
