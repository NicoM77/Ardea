# CSH Alert System — frontend research (state as of 2026-11-18)

Research only; nothing was installed or created in any repository. Version numbers and dates marked "(npm)" were read directly from the npm registry on 2026-11-18. Items that could not be confirmed against a primary source are marked **UNVERIFIED**.

Contents: 1 Angular · 2 Component libraries · 3 Tailwind · 4 Server state · 5 OpenAPI client generation · 6 i18n · 7 PWA + Web Push · 8 Capacitor · 9 Self-hosted push · 10 Accessibility · 11 Fonts and icons · 12 Testing

---

## 1. Angular — current state

### Versions and support window

| Major | Status | Released | Active support ends | LTS ends |
|---|---|---|---|---|
| **22** | **Active** | 2026-06-03 | 2027-06 | 2028-06 |
| 21 | LTS | 2025-11-19 | 2026-06-03 | 2027-06 |
| 20 | LTS (ends in 10 days) | 2025-05-28 | 2025-11-19 | 2026-11-28 |
| ≤ 19 | unsupported | | | |

- Latest patch (npm): `@angular/core` **22.2.6** (2026-11-12), `@angular/cli` 22.2.7; `22.3.0-rc.0` is on the `next` tag. `@angular/cdk`, `@angular/material`, `@angular/aria` **22.3.0** were published today (2026-11-18).
- **Cadence changed with v22: one major per year (June), minors roughly every two months, 12 months active + 12 months LTS.** v23 is scheduled for "~June 2027". Older material that says "a major every six months" is out of date.
- Toolchain for v22: Node `^22.22.3 || ^24.15.0 || ^26.0.0` (Node 20 dropped); **TypeScript `>=6.0 <6.1`** (TypeScript 7.0, the native compiler, is `latest` on npm since 2026-07 but is **not** supported by Angular 22 — pin TS 6.0.x); RxJS 6.5.3+/7.4+ (still a dependency). `@angular/build` 22.2 bundles Vite 8, esbuild 0.28 and Rolldown (used for chunk optimisation since 22.1).

**Start a new project on Angular 22.** It is the only Active major, has ~7 months of active support left and LTS until mid-2028; v23 arrives June 2027 and is a normal `ng update`.

### Feature status in v22

| Feature | Status in 22.x | Notes |
|---|---|---|
| Zoneless change detection | **Stable** (since 20.2), **default since v21** | New apps ship without zone.js; `zone.js` is an optional peer dependency. |
| `OnPush` as default strategy | **Default since v22** | `ChangeDetectionStrategy.Default` renamed `Eager`. |
| Signals core (`signal`, `computed`, `effect`, `linkedSignal`) | Stable (v20) | |
| Signal inputs/outputs/model, signal queries (`input()`, `output()`, `model()`, `viewChild()`…) | **Stable** (graduated by v20) | Decorator `@Input/@Output/@ViewChild` still work but are the legacy style. There is no separate "signal components" authoring format — it is just these APIs + zoneless + OnPush. |
| **Signal Forms** (`@angular/forms/signals`) | **Stable since v22** (experimental in v21) | Interop with reactive forms; integrates with Angular Aria. |
| `resource` / `rxResource` / `httpResource` | **Stable since v22** (experimental in 19–21) | v22 adds `chain()` for dependent resources. Official guidance: use `httpResource` for reads only, `HttpClient` for mutations. `debounced()` is still experimental. |
| `HttpClient` on `fetch` | Default since v22 (`withFetch()` deprecated as redundant) | Upload-progress reporting changed (`reportUploadProgress`). |
| Built-in control flow `@if/@for/@switch`, `@let` | Stable | |
| `@defer` | Stable (since v18) | Incremental hydration is the SSR default in v22 (irrelevant for a pure SPA). |
| `application` builder (esbuild + Vite dev server) | **Stable, default** | Webpack `browser` builder is deprecated. |
| **Vitest** as test runner | **Stable and default since v21** | jsdom by default; real-browser mode via Playwright or WebdriverIO provider. Karma "still supported", with a `migrate-karma-to-vitest` schematic. |
| **Angular Aria** (`@angular/aria`) | **Stable since v22** (developer preview in v21) | Headless directives: Accordion, Combobox (+ documented Autocomplete/Select/Multiselect compositions), Listbox, Grid, Menu/Menubar, Tabs, Toolbar, Tree. Ships test harnesses. MIT. |
| **Angular CDK** | Stable | a11y (FocusTrap, LiveAnnouncer, FocusMonitor), overlay, dialog, stepper, table, tree, scrolling (virtual scroll), drag-drop, listbox, menu, layout, testing harnesses. |
| Router resources, `@boundary` error boundaries | Developer preview (22.2) | Do not build the plan on these. (Status taken from secondary coverage — treat as **UNVERIFIED**.) |
| `@Service` decorator, `injectAsync()` | New in v22, unlabelled | Optional sugar; `@Injectable` still fine. |
| Rolldown chunk optimisation | Stable/default (22.1) | |
| Angular MCP server / AI tooling | Stable | Useful for AI-assisted development; not a runtime concern. |

### Things older tutorials still recommend that are now legacy or deprecated

- **`*ngIf`, `*ngFor`, `*ngSwitch`** — deprecated since v20 ("intent to remove in a future major"); use `@if/@for/@switch`. Custom structural directives are not deprecated.
- **NgModules** — not deprecated, but standalone has been the default since v19; a new app should have no `AppModule`, no `CommonModule` imports, and use `bootstrapApplication` + `provide*()` functions. `HttpClientModule` is deprecated (use `provideHttpClient()`).
- **zone.js / `NgZone` tricks / `fakeAsync`** — zoneless is default; zone-based test helpers need an extra polyfill and are on the way out. Write tests with `await fixture.whenStable()` and Vitest fake timers.
- **Karma + Jasmine** — Karma (the project itself is deprecated upstream) is no longer the default; new projects get Vitest. Protractor is long gone.
- **`@angular/animations`** — deprecated since 20.2 in favour of native CSS with `animate.enter` / `animate.leave` (deprecation confirmed on angular.dev; the removal version is not stated there — **UNVERIFIED**). Do not add `provideAnimations()`.
- **Webpack builder (`@angular-devkit/build-angular:browser`)** — deprecated; use `@angular/build:application`.
- **`@Input()`/`@Output()`/`@ViewChild()` decorators, constructor injection, `ngOnChanges`** — still supported but superseded by `input()`, `output()`, `viewChild()`, `inject()`, `computed()`/`effect()`.
- **Template-driven and classic reactive forms** — still supported; for a greenfield v22 app Signal Forms is the forward path.
- **`withFetch()`, `withIncrementalHydration()`, JSONP support** — deprecated in v22/22.1 (now defaults or obsolete).
- **`ChangeDetectionStrategy.OnPush` boilerplate in every component** — unnecessary in v22 (it is the default).

Sources:
- https://angular.dev/reference/releases (support table, yearly cadence, v23 date)
- https://angular.dev/reference/versions (Node/TypeScript/RxJS compatibility)
- https://angular.dev/roadmap (Signal Forms, resource, Aria stable; zoneless default; OnPush default)
- https://blog.ninja-squad.com/2026/06/03/what-is-new-angular-22.0 and https://blog.ninja-squad.com/2026/07/29/what-is-new-angular-22.1
- https://angular.dev/guide/zoneless · https://angular.dev/guide/testing · https://angular.dev/guide/aria/overview · https://angular.dev/api/common/NgIf · https://angular.dev/tools/cli/build-system-migration · https://angular.dev/guide/http/http-resource · https://angular.dev/guide/animations/migration
- npm registry: https://www.npmjs.com/package/@angular/core , https://www.npmjs.com/package/@angular/aria

**Implication for the plan:** Target Angular 22 (TypeScript 6.0, Node 22 or 24 LTS) with the v22 defaults — standalone, zoneless, OnPush, signals, Signal Forms, `httpResource` for reads, Vitest — and state explicitly in the plan that NgModules, `*ngIf`, zone.js, Karma and `@angular/animations` are not to be used. Budget one `ng update` to v23 in mid-2027.

---

## 2. Component library for a custom, accessible design system

### Headline changes since late 2025

1. **Angular Aria is stable (v22)** — first-party headless a11y primitives now exist, so "CDK + own components" is no longer a build-everything-yourself option.
2. **spartan/ui reached 1.0 in June 2026** (now 1.9.0) — stable, semver'd `brain` (headless) + copy-in `helm` (Tailwind 4 styles).
3. **PrimeNG is no longer open source from v22** (July 2026): the GitHub repo was archived 2026-06-28; v22+ ships compiled under the commercial "PrimeUI" licence and requires a licence key (free Community licence only for individuals / very small organisations, renewed annually; redistribution "so that third parties can develop with it" needs an OEM licence). v21 is the last MIT version. A community fork (OpenNG "Optimus UI", working name `open-prime`) exists but is young. **For an open-source, self-hosted project whose users build from source, PrimeNG ≥ 22 is effectively ruled out.**
4. **TanStack Table has a stable Angular adapter (v9)** and TanStack Query now publishes a non-experimental `@tanstack/angular-query` package (see §4).

### Comparison

| Option | Version (npm, date) | Licence | Restyling to a non-Material/custom look | A11y documentation |
|---|---|---|---|---|
| (a) **Angular Material** | 22.3.0 (2026-11-18) | MIT | M3 only (M2 themes kept for back-compat, "will be removed"). `mat.theme()` emits `--mat-sys-*` CSS variables; every component has a `mat.<component>-overrides()` mixin for token-level changes (colours, shape/corner radius, typography, elevation). Density scale 0…-5. **But:** DOM structure and CSS classes are declared private, so anything not exposed as a token (layout of form-field, floating labels, ripples, state layers, FAB/chip shapes, M3 motion) cannot be changed supportedly. You can make it square and re-coloured; you cannot make it stop feeling like Material. | Per-component a11y notes; follows WAI-ARIA APG; no published WCAG conformance report/VPAT. Roadmap lists ongoing "accessibility reviews against WCAG". |
| (b) **Angular CDK + Angular Aria + own components** | 22.3.0 (2026-11-18) | MIT | Fully headless — you own all markup and CSS. Works with plain CSS or Tailwind. | Aria implements WAI-ARIA APG patterns (keyboard, roles, focus); positioned as the choice for "WCAG-oriented components with custom styling". No formal conformance report. |
| (c) **spartan/ui** (`@spartan-ng/brain` + helm) | 1.9.0 (2026-10-29); 1.0 in 2026-06 | MIT | `brain` is headless; `helm` is shadcn-style code copied into your repo and fully editable. **`helm` assumes Tailwind 4** (`tailwindcss >=4` is a peer dependency) and a shadcn token vocabulary; brain peers on `@angular/cdk`. 65 documented components. | "Accessibility you can rely on" (ARIA, keyboard, focus in brain); no WCAG conformance statement. |
| (d) **PrimeNG** | 22.1.5 (2026-11-11) | **Commercial (PrimeUI licence, key required)**; ≤ v21 MIT | Good technically: `unstyled` mode + pass-through (PT) API, design tokens via `@primeuix/themes`. | Best documented of the styled kits (per-component accessibility sections targeting WCAG 2.1 AA). |
| (e) **Taiga UI** | 5.33.0 (2026-11-16); v5.0 April 2026 | Apache-2.0 | Signals, CSS animations, Angular ≥ 19. Themable through CSS variables and "appearances", but it is an opinionated design language (rounded, T-Bank look); squaring everything and removing shadows means fighting many component styles. | States components "support keyboard navigation, screen readers"; no WCAG conformance document. |
| (f) **ng-zorro-antd** | 22.2.0 (2026-11-04) | MIT | Ant Design look; theming is Less-variable based (CSS-variable "dynamic theme" still labelled experimental in docs). Hard to move away from the Ant look. | Weakest: no accessibility conformance documentation; long-standing a11y issues. |
| (g) **ng-primitives** (Angular Primitives) | 0.131.4 (2026-10-28) — still 0.x | Apache-2.0 | Headless, state exposed via data-attributes; styling-agnostic (no Tailwind requirement). Broad: combobox, select, listbox, dialog, date-picker, toast, tabs, menu, pagination, form-field, focus-trap… | APG-based; no conformance statement. Pre-1.0, frequent releases. |
| (g) **Radix NG** (`@radix-ng/primitives`) | 1.3.0 (2026-11-08) | MIT | Headless port of Radix; has stepper, calendar/date-field, toast, combobox, signal-forms integration. Small user base (~3k downloads/week vs 121k for spartan brain, 189k for Angular Aria). | Radix-style APG adherence; no conformance statement. |
| (g) **TanStack Table / Virtual** (`@tanstack/angular-table` 9.3.2, `@tanstack/angular-virtual` 6.1.12) | 2026-11 | MIT | Headless table logic (sorting, filtering, selection, column visibility) and virtualisation; you render a native `<table>`. | You own the semantics (native table = good baseline). |
| Kendo UI for Angular | commercial | paid | — | Only Angular kit found with a published VPAT/ACR against WCAG 2.2; listed for completeness, not suitable for an OSS project. |

Weekly npm downloads (2026-11-10…16) for a rough adoption signal: Angular Material 3.19 M · PrimeNG 905 k · Angular Aria 189 k · ng-zorro 183 k · spartan brain 121 k · Taiga core 42 k · ng-primitives 37 k · Radix NG 3 k.

### Coverage of the components the app needs

| Need | Material | CDK + Aria | spartan/ui | Taiga UI | ng-zorro | ng-primitives | Radix NG |
|---|---|---|---|---|---|---|---|
| Data table + sortable headers | `mat-table` + `matSort` | `CdkTable` (headless) or native `<table>` + own sort button (`aria-sort`); Aria **Grid** only for interactive/editable cell grids — docs say use native `<table>` for read-only data | Data Table (built on TanStack Table) | yes | yes | — (use TanStack) | — (use TanStack) |
| Virtual scroll | CDK virtual scroll (table integration is still experimental in `cdk-experimental/scrolling`) | `cdk-virtual-scroll-viewport`; or TanStack Virtual | via TanStack Virtual | yes | yes (`nzVirtualScroll`) | — | — |
| Combobox / autocomplete | Autocomplete | **Aria Combobox + Listbox** | Combobox, Autocomplete, Command | yes | yes | yes | yes |
| Multi-select | Select `multiple`, Chips | **Aria Multiselect** pattern | Select (multiple), Combobox | yes | yes | yes | yes |
| Dialog | Dialog | **CDK Dialog** (focus trap, restore, `aria-modal`) | Dialog, Alert Dialog, Sheet, Drawer | yes | yes | yes | yes |
| Stepper (setup wizard) | Stepper | **CDK Stepper** (headless) | no primitive; a "stepper" Block only | yes | Steps | — | Stepper |
| Date picker | Datepicker, Timepicker | — (none; use native `<input type="date">` or build on Aria Grid) | Calendar, Date Picker | yes | yes | Date Picker | Calendar, Date Field |
| Toast | Snackbar | — (build with CDK Overlay + `LiveAnnouncer`; ~100 lines) | Sonner | Alerts/Toast | Message/Notification | Toast | Toast |
| Tabs | Tabs | **Aria Tabs** | Tabs | yes | yes | Tabs | Tabs |
| Tree | Tree | **Aria Tree** / CDK Tree | — | yes | yes | — | — |
| Menu | Menu | Aria Menu / CDK Menu | Dropdown/Context menu | yes | yes | Menu | Menu |

**Documented WCAG conformance:** none of the open-source options publishes a formal WCAG 2.2 conformance report. PrimeNG's per-component accessibility pages are the closest (WCAG 2.1 AA oriented) but PrimeNG is no longer open source. In every case, conformance of the finished app must be established by the project's own testing (§10) — a library can only supply correct roles/keyboard/focus behaviour.

### Recommendation (small team, custom square/ruled design)

**Primary: (b) own component layer on Angular CDK + Angular Aria, styled with plain CSS custom properties (or Tailwind, see §3), plus TanStack Table for the alert table.**

- Why: first-party, stable, MIT, same release train as the framework (no third-party lag on `ng update`), zero imposed look, ships test harnesses, and covers the hard interaction patterns (combobox/select/multiselect, tabs, tree, menu, dialog focus-trap, stepper logic, overlay positioning, virtual scroll). The custom design is deliberately plain (ruled layouts, square controls, no shadows) — native `<button>`, `<select>`, `<input>`, `<table>`, `<dialog>`-style semantics cover a large share with no library at all.
- Cost: you write the visual layer and a handful of widgets yourself — toast (Overlay + LiveAnnouncer), date picker (recommend native `<input type="date">`/`datetime-local` first; it is accessible and localised for free), sortable table header. Estimate ~15–20 small components.
- Gaps to plan for: no date picker, no toast, no formal conformance doc.

**Alternative: (c) spartan/ui 1.x** if the team would rather start from working styled code and delete/adjust: you get Data Table, Date Picker, Sonner, Combobox ready-made and own the `helm` source. Trade-offs: commits you to Tailwind 4 and the shadcn class/token vocabulary (you will rewrite most `helm` classes to remove radii/shadows/cards), adds a third-party dependency that must keep pace with Angular majors (brain currently peers `>=21 <23`), no stepper primitive or tree. A hybrid is viable: CDK/Aria as the base, borrow individual spartan pieces (e.g. calendar).

**Not recommended:** Angular Material (can be squared and re-coloured through tokens but remains recognisably Material and its DOM is private — a poor fit for "ruled, no cards/shadows"); Taiga UI and ng-zorro (opinionated looks, weaker a11y documentation); **PrimeNG ≥ 22 (licence incompatible with an open-source self-hosted project; pinning MIT v21 means building on a frozen line that only supports Angular 21)**; ng-primitives (attractive, but still 0.x); Radix NG (small community).

Sources:
- Angular Aria: https://angular.dev/guide/aria/overview · https://angular.dev/guide/aria/grid · https://github.com/angular/components/tree/main/src/aria
- Angular Material theming: https://github.com/angular/components/blob/main/guides/theming.md (also https://material.angular.dev/guide/theming)
- spartan/ui: https://www.spartan.ng/documentation/changelog · https://dev.to/playfulprogramming-angular/announcing-spartanui-10-546o · https://www.npmjs.com/package/@spartan-ng/brain
- PrimeNG licence: https://unpkg.com/primeng@22.1.5/LICENSE.md · https://primeui.dev/licenses/community · https://dev.to/playfulprogramming-angular/ng-news-2617-primengs-new-licensing-and-a2ui-for-angular-4eik · unstyled mode https://primeng.org/theming/unstyled
- Taiga UI v5: https://dev.to/angular/whats-new-in-taiga-ui-v5-a-modern-angular-ui-kit-7lm · https://github.com/taiga-family/taiga-ui/releases
- ng-zorro: https://ng.ant.design/docs/changelog/en · https://ng.ant.design/docs/customize-theme/en
- ng-primitives: https://www.npmjs.com/package/ng-primitives · Radix NG: https://www.npmjs.com/package/@radix-ng/primitives · TanStack Table: https://www.npmjs.com/package/@tanstack/angular-table

**Implication for the plan:** Plan a small in-house component library (`ui/`) on `@angular/cdk` + `@angular/aria` with TanStack Table, and schedule explicit work items for toast, date/time input and the level-tile; drop PrimeNG from consideration on licence grounds. Keep spartan/ui as the documented fallback if the team prefers copy-in components and accepts Tailwind.

---

## 3. Tailwind CSS vs plain CSS custom properties

- **Current major: Tailwind CSS 4** — `tailwindcss` **4.3.5** (npm, 2026-11-06); 4.0 shipped January 2025, 4.3 on 2026-05-08. MIT. No v5 has been announced (searched; nothing found — **UNVERIFIED** absence).
- **Angular CLI integration today:** officially documented at angular.dev. `ng add tailwindcss` does the whole setup; manually it is `npm i tailwindcss @tailwindcss/postcss postcss`, a `.postcssrc.json` with the `@tailwindcss/postcss` plugin, and `@import 'tailwindcss';` in `src/styles.css`. `@angular/build` 22 declares `tailwindcss ^2 || ^3 || ^4` as an optional peer. There is no `tailwind.config.js` any more in v4 — configuration is CSS-first. (The `@tailwindcss/vite` plugin is not used; the Angular CLI does not expose its Vite config for production builds.)
- **Design tokens:** `@theme { --color-…; --radius-…; --font-…; --shadow-…; --spacing-… }` defines tokens that are (a) emitted as normal CSS custom properties on `:root` and (b) generate the matching utilities. Defaults can be removed per namespace (`--color-*: initial;`, `--radius-*: initial;`, `--shadow-*: initial;`) or entirely (`--*: initial;`), so the custom system's five-step level ramp, 0/2 px radii and absence of shadows can be enforced at the token level. `@theme inline` and `@theme static` exist for aliasing and always-emit behaviour.
- **Caveats with Angular:** utilities work in templates as usual; using `@apply` or theme references inside component-scoped stylesheets needs `@reference "../../styles.css";` in each file (v4 behaviour), which is friction if most styling lives in component CSS. Tailwind v4 targets modern browsers (uses `@property`, `color-mix()`, cascade layers) — fine for an evergreen-only app and current WebViews.

**Is plain CSS a reasonable alternative? Yes — for this project arguably the better default.** The design is a small, closed system (one type family, one mono, five level colours, two radii, rules instead of cards), and with option (b) from §2 the team writes its own components anyway. Angular's emulated view encapsulation gives component-scoped styles for free; modern CSS has native nesting, `@layer`, `:has()`, container queries, `color-mix()`, `light-dark()` and logical properties. A single `tokens.css` with custom properties plus ~15 component stylesheets has no build dependency, no class-string noise in templates, and nothing to upgrade. Tailwind earns its place when (i) the team already thinks in utilities, (ii) there are many one-off layouts, or (iii) spartan/ui `helm` is adopted (it requires Tailwind 4).

Sources:
- https://angular.dev/guide/tailwind
- https://tailwindcss.com/docs/theme
- https://tailwindcss.com/blog/tailwindcss-v4-3
- https://www.npmjs.com/package/tailwindcss

**Implication for the plan:** Decide Tailwind together with the §2 choice: CDK/Aria + own components → plain CSS custom properties with a `tokens.css` and cascade layers (no Tailwind); spartan/ui → Tailwind 4 with `@theme` and defaults reset. Either way the design tokens are CSS variables, so the decision is reversible.

---

## 4. Server state / data fetching

| Option | Version (npm) | Status | Notes |
|---|---|---|---|
| **`httpResource` / `resource` / `rxResource`** (Angular core) | 22.2.6 | **Stable since v22** | Signal-based reads with `value()`, `status()`, `error()`, `isLoading()`, `reload()`; auto-cancels the previous request when inputs change; goes through `HttpClient` interceptors. **No shared cache, no dedup across components, no stale-while-revalidate, no mutation/invalidation model** — official docs say to use `HttpClient` directly for POST/PUT/DELETE. |
| **TanStack Query for Angular** | `@tanstack/angular-query` **5.108.0** (first stable `5.0.0` published **2026-11-17**, i.e. yesterday; RCs since 2026-09-22). The long-lived `@tanstack/angular-query-experimental` (5.107.2) still has ~70× the downloads. | **Just became stable** | Requires Angular ≥ 20.1. Gives query cache, dedup, background refetch, polling, retry, mutations + invalidation, optimistic updates, devtools. Extra dependency and concepts. Treat "stable" as very fresh. |
| **NgRx SignalStore** (`@ngrx/signals`) | 22.0.1 (2026-09-10) | Stable core; **resource integration is experimental** (`@ngrx/signals/resource` entry point new in v22); Events plugin was introduced as experimental (current status **UNVERIFIED**) | Good for structured client state; `withResource` in the community NgRx Toolkit. Most popular state library (~930 k downloads/week) but more than this app needs. |

**Community direction (2026):** with resources stable, the mainstream advice is "signals + services first": `httpResource` for reads, plain `HttpClient` for writes followed by `resource.reload()`, small injectable services (or `linkedSignal`) for the little shared UI state there is. Add TanStack Query only when you need cross-view caching/polling/optimistic updates; add SignalStore only when client state becomes non-trivial.

**Minimal recommendation for an alerting dashboard:**
- Reads: `httpResource` in feature-level data services (one per aggregate: alerts, sources, settings).
- Writes: `HttpClient` methods returning Promises/Observables; on success call `.reload()` on the affected resources (or `.update()`/`.set()` for local optimistic patches).
- Live updates: the alert list wants freshness. Prefer a server push channel (SSE or SignalR from ASP.NET Core) that triggers `reload()`, rather than a client-side polling/cache layer. If the plan instead wants polling with focus-refetch and retry, that is exactly TanStack Query's feature set — re-evaluate then.
- No global store at the start.

Sources:
- https://angular.dev/guide/http/http-resource · https://angular.dev/roadmap
- https://tanstack.com/query/latest/docs/framework/angular/overview · https://www.npmjs.com/package/@tanstack/angular-query
- https://infoq.com/news/2026/10/ngrx-22-angular · https://www.npmjs.com/package/@ngrx/signals

**Implication for the plan:** Start with built-in `httpResource` + `HttpClient` and no state library; record TanStack Query (now with a stable Angular package) as the upgrade path if polling/caching needs grow, and keep data access behind per-feature services so that swap stays local.

---

## 5. API client generation from OpenAPI 3.1

Context: ASP.NET Core's built-in `Microsoft.AspNetCore.OpenApi` emits **OpenAPI 3.1** by default since .NET 10 (backend research should confirm the exact behaviour for the chosen .NET version); the document can be written at build time, so the Angular client can be generated in CI without a running server.

| Tool | Version (npm, date) | Maintained? | OpenAPI 3.1 | Output for Angular | Signals / `httpResource` |
|---|---|---|---|---|---|
| **orval** (+ `@orval/angular`) | **8.48.0** (2026-11-14); weekly releases | **Yes, very active** (~4 M downloads/week) | Yes (v8) | Injectable `HttpClient` services; optional Zod runtime validation; MSW mocks | **Yes — first-class**: `override.angular.retrievalClient: 'httpResource' \| 'httpClient' \| 'both'`. `both` gives signal-first `*.resource.ts` readers for GETs and `HttpClient` service methods for mutations — precisely the split Angular recommends. Needs Node ≥ 22.18, ESM config. MIT. |
| **OpenAPI Generator** `typescript-angular` | generator 7.27.0 (2026-11-02); npm wrapper 2.42.2 | Yes | Partial/"beta" 3.1 handling (**UNVERIFIED** for edge cases such as type arrays) | Classic injectable services returning Observables; `ngVersion` up to 22 | **Yes, opt-in**: `withHttpResource=true` adds a resource method per GET (Angular 20+). Requires a JVM (or Docker) in the toolchain. Apache-2.0. |
| **@hey-api/openapi-ts** (`@angular/common` plugin + Angular client) | **0.99.0** (2026-06-22) | **Uncertain**: weekly releases until 2026-06-22, none since; last commit on the repo 2026-09-30; still 0.x | Yes | Per-endpoint `HttpRequest` builders and services | **Yes, but plugin labelled Beta**: generates `…Resource()` functions wrapping `httpResource`. Docs page is titled "Angular v20". MIT. |
| **ng-openapi-gen** | 1.1.0 (2026-09-23) | Yes, slow cadence | Yes (3.0 + 3.1) | Angular-specific: one function per operation + `Api` injectable (Promise by default, Observable optional), models as interfaces; Angular ≥ 16 | **No** `httpResource` output; you wrap calls yourself. MIT. |
| **openapi-typescript** (+ `openapi-fetch`) | 7.13.0 (2026-02-11) | Types generator maintained; **`openapi-fetch` put into maintenance mode** (2026 roadmap). Peer dep is `typescript ^5.x` — **conflicts with the TypeScript 6 that Angular 22 requires** until v8 ships | Yes (3.0/3.1) | Types only (`paths`, `components`); zero runtime | No Angular integration; `openapi-fetch` bypasses `HttpClient` (no interceptors, no `HttpTestingController`). You would hand-write a thin typed wrapper over `HttpClient`/`httpResource`. MIT. |
| **Kiota** (Microsoft) | CLI 1.38.0 (2026-11-02); TS runtime packages `1.0.0-preview.114` | Yes, but **TypeScript support is still preview** | Yes | Fluent request-builder client on its own fetch-based request adapter, with its own auth/serialization abstractions | No — does not use `HttpClient`, interceptors or signals; heavy runtime for a single-API SPA. MIT. |
| ng-openapi (`ng-openapi`, `@ng-openapi/http-resource`) | 0.5.0 (2026-10-29) | Young, small (≈6 k/week) | claimed | Angular services + httpResource plugin | Yes, but 0.x / single maintainer — not recommended as a foundation. |

**Recommendation:** **orval 8 with `client: 'angular'`, `retrievalClient: 'both'`** — actively maintained, OpenAPI 3.1, generates through Angular's `HttpClient` (so interceptors for auth and base-URL rewriting keep working — important for the user-configurable server URL in the Capacitor build), and is the only mainstream tool whose default output matches "resources for reads, HttpClient for writes". Runner-up: OpenAPI Generator `typescript-angular` with `withHttpResource` (very stable, but JVM dependency and heavier output). Avoid basing the plan on hey-api (stalled releases, beta plugin), openapi-fetch (maintenance mode, TS 6 peer conflict, bypasses HttpClient) or Kiota (TS preview).

Practical notes: generate into a git-ignored or committed `src/app/api/generated/` from the build-time OpenAPI document; run generation in CI and fail on diff; do not let the generator own the base URL — inject it via an interceptor/`InjectionToken` so it can change at runtime.

Sources:
- orval: https://www.npmjs.com/package/orval · https://cdn.jsdelivr.net/npm/@orval/angular@8.48.0/README.md · https://orval.dev/guides/angular · https://orval.dev/guides/migration-v8
- OpenAPI Generator: https://openapi-generator.tech/docs/generators/typescript-angular/ · https://github.com/OpenAPITools/openapi-generator/releases
- hey-api: https://heyapi.dev/docs/openapi/typescript/plugins/angular/v20.md · https://github.com/hey-api/hey-api/releases · https://www.npmjs.com/package/@hey-api/openapi-ts
- ng-openapi-gen: https://github.com/cyclosproject/ng-openapi-gen
- openapi-typescript roadmap: https://github.com/openapi-ts/openapi-typescript/discussions/2559
- Kiota: https://github.com/microsoft/kiota/releases · https://www.npmjs.com/package/@microsoft/kiota-bundle

**Implication for the plan:** Make "OpenAPI document produced at backend build → orval generates the Angular client" a pipeline step, with the server base URL supplied by an interceptor rather than baked into generated code. Pin the generator version and commit its config.

---

## 6. Internationalisation (runtime-switchable, one Docker image + Capacitor)

| | **Transloco** (`@jsverse/transloco`) | **ngx-translate** (`@ngx-translate/core`) | **`@angular/localize`** (built-in) |
|---|---|---|---|
| Version (npm) | **8.4.0** (2026-06-13); `9.0.0-alpha.4` (2026-09-26) on `next` | **18.1.0** (2026-10-27); v18.0 stable 2026-06 | 22.2.6 |
| Maintenance | Active (jsverse org); v9 in alpha with breaking changes pending | **Active again and modernised** (CodeAndWeb); v18 is the only supported major, v17 critical fixes only | Angular team; no announced runtime-switching roadmap |
| Weekly downloads | ~477 k | ~1.86 M | ~1.92 M |
| Model | Runtime; JSON per language loaded over HTTP | Runtime; JSON per language loaded over HTTP | **Compile-time**: one build output per locale, locale chosen by URL/server; switching = full page reload into another bundle |
| Signals | `translateSignal()`, `translateObjectSignal()`; structural directive `*transloco` and pipe | v18 rebuilt on signals: `currentLang` is a signal, standalone `translate()` returns a signal, pipe/directive signal-driven; NgModule API removed (`provideTranslateService()`) | n/a (static) |
| Lazy loading | Yes — per language and per **scope** (feature) | Yes — per language; `provideChildTranslateService()` gives a feature its own loader/store; http-loader merges multiple files | Per-locale bundles |
| ICU plurals/select | Official plugin `@jsverse/transloco-messageformat` (8.4.0, uses `@messageformat/core`) | Third-party compiler `ngx-translate-messageformat-compiler` 7.3.0 (supports core v18); no built-in ICU | **Built-in ICU** in templates |
| Locale formatting | `@jsverse/transloco-locale` (dates/numbers/currency via `Intl`) | none (use `Intl` / Angular pipes) | Angular pipes with locale data |
| Missing-key tooling | `@jsverse/transloco-keys-manager` (extract + find missing/extra keys; being merged into the monorepo for v9) | third-party extractors; `hasTranslationKey()` in 18.1 | `ng extract-i18n` (XLIFF/JSON/ARB), build fails on missing with `i18nMissingTranslation: error` |
| Fallback chain | `fallbackLang`, missing handler | `fallbackLangs` ordered chain (18.1) | n/a |
| Type-safe keys | **Not built in** for either runtime library (searched; nothing first-party found — **UNVERIFIED** for Transloco 9). Common pattern: treat `en.json` as the source of truth, derive a key union type from it (`keyof` + path helper) and wrap the translate function/pipe; plus a CI check with the keys manager. | same | Type safety by construction (messages live in templates) |

**Why not `@angular/localize` here:** one bundle per locale means the single Docker image must serve N builds behind locale paths, language switching is a navigation, and a Capacitor app would have to ship every locale build inside the app and reload the WebView to switch. It is the best choice for SSR/SEO sites, not for a self-hosted SPA + mobile wrapper with a user-level language setting.

**Recommendation: Transloco 8** (runtime, lazy per language and per feature scope, official ICU plugin, official key extraction/validation, locale plugin, signal API). ngx-translate 18 is now a legitimate alternative — bigger install base, fully signal-based — but ICU still depends on a third-party compiler and there is no official extraction tool. If Transloco is chosen, pin v8 now and expect a v9 migration (schematic provided; `translocoRead`/`read` are removed, so avoid them from day one).

Practical notes for the plan:
- Bundle `en.json` into the app as the synchronous fallback (no flash of keys offline or in Capacitor); lazy-load `de.json`, later `fr.json`/`it.json`, from app assets.
- Use ICU `plural`/`select` from the start — German/French/Italian plural and gender agreement will otherwise force string concatenation.
- Format dates, numbers and relative times with `Intl` (`Intl.DateTimeFormat`, `Intl.RelativeTimeFormat`) keyed off the active language; set `<html lang>` and announce language changes for assistive tech.
- Server-generated text (alert titles, push notification bodies, e-mails) needs its own localisation on the ASP.NET Core side — push payloads are rendered by the OS without the Angular app running.

Sources:
- Transloco: https://github.com/jsverse/transloco/releases · https://jsverse.gitbook.io/transloco/core-concepts/signals · https://www.npmjs.com/package/@jsverse/transloco
- ngx-translate: https://ngx-translate.org/20-reference/90-release-notes/ · https://ngx-translate.org/getting-started/migration-guide/ · https://www.npmjs.com/package/@ngx-translate/core
- Angular i18n: https://angular.dev/guide/i18n

**Implication for the plan:** Use a runtime i18n library (Transloco 8 with the messageformat plugin and keys-manager in CI) and rule out `@angular/localize`; add a small typed-key wrapper and a "no missing keys" CI gate, and plan separate server-side localisation for notification text.

---

## 7. PWA and Web Push

### `@angular/service-worker` (22.2.6, stable, MIT)
- Still the first-party way to make an Angular app installable/offline (`ng add @angular/pwa`, `ngsw-config.json`). `SwPush` exposes: `requestSubscription({ serverPublicKey })` (VAPID), `unsubscribe()`, `subscription`, `messages`, `notificationClicks`, **`notificationCloses`** and **`pushSubscriptionChanges`** (the last two are additions that older tutorials do not mention), `isEnabled`.
- The Angular service worker shows a notification automatically when the push payload has the shape `{ "notification": { "title", "body", "icon", "badge", "tag", "actions": [...], "data": { "onActionClick": { "default": { "operation": "navigateLastFocusedOrOpen", "url": "/alerts/123" }, "<action>": {...} } } } }`. Supported click operations: `openWindow`, `focusLastFocusedOrOpen`, `navigateLastFocusedOrOpen`, `sendRequest` (simple GET only). URLs resolve relative to the SW scope.
- Limits: no hook for custom logic inside the push handler (e.g. decrypt, fetch details, set badge, suppress) — for that you ship a small custom service worker that `importScripts('./ngsw-worker.js')` and adds its own `push` listener. The backend must produce exactly the payload format above (ASP.NET Core side: a VAPID Web Push library).

### Platform support matrix

| Platform | Web Push | Conditions |
|---|---|---|
| Chrome/Edge desktop, Firefox desktop | Yes | Service worker + VAPID; permission prompt needs a user gesture in practice (browsers quiet/penalise unsolicited prompts). |
| Safari macOS | Yes (standards-based since Safari 16 on macOS 13 Ventura) | Works in regular tabs; subscription needs an explicit user gesture and `userVisibleOnly: true`; Declarative Web Push since Safari 18.5. |
| Android Chrome (and Firefox, Samsung Internet) | Yes | No install required, though an installed PWA gets better treatment; delivered via FCM under the hood. |
| **iOS / iPadOS** | **Yes, from 16.4, only for a web app added to the Home Screen** | Manifest `display: standalone` or `fullscreen`; permission must be requested from a direct user interaction inside the installed web app; no push in a normal Safari tab or in other browsers' tabs; no programmatic install prompt (user must use Share → Add to Home Screen). Since iOS 26 any site added to the Home Screen opens as a web app by default. Third-party browsers can offer Add to Home Screen, but the web app still runs on WebKit. |
| iOS in the **EU** | Same as elsewhere | Apple announced in Feb 2024 that Home Screen web apps would be removed in the EU for iOS 17.4 (DMA), then **reversed before release** (1 March 2024); they remain available, WebKit-only. No later change found — current-2026 status **UNVERIFIED** beyond that reversal. |
| **Capacitor WebView (iOS WKWebView / Android WebView)** | **No** | Service-worker Web Push is not available inside the native app's WebView → the wrapped app must use native push (§8, §9). |

### Declarative Web Push
- Apple/WebKit proposal now on the W3C/WHATWG standards track (reported as a Working Draft — **UNVERIFIED** exact stage): push message is JSON with `"web_push": 8030` and a `notification` object (`title`, `body`, `navigate` URL (required), `app_badge`, …). The browser shows it **without running a service worker**; `window.pushManager` allows subscribing without a service worker.
- Shipping: iOS/iPadOS 18.4 (Home Screen web apps), Safari 18.5 on macOS. **Chrome and Firefox have not shipped it** (no primary source found saying otherwise — **UNVERIFIED**).
- Backward compatible by design: if a service worker with a `push` handler exists, the event still fires; browsers without support just see a JSON payload. A payload can therefore be made dual-format, but the Angular SW expects a top-level `notification` object too — the two shapes can coexist in one JSON document. Worth doing for iOS reliability (no silent-push penalties, no SW start-up failures), but it is an optimisation, not a prerequisite.

### Badging API (`navigator.setAppBadge` / `clearAppBadge`)
- Supported: Chrome/Edge desktop 81+ (installed PWAs, Windows/macOS), Safari macOS 17+ (web apps added to Dock), **iOS/iPadOS 16.4+ Home Screen web apps** (requires notification permission to display).
- Not supported: **Chrome for Android** (Android shows its own notification dot instead), Firefox, Samsung Internet. "Limited availability" (not Baseline).
- Declarative Web Push can set `app_badge` from the payload on Apple platforms. In Capacitor use a native badge plugin (`@capawesome/capacitor-badge` 8.0.4, MIT).

Sources:
- https://angular.dev/ecosystem/service-workers/push-notifications · https://angular.dev/api/service-worker/SwPush
- https://webkit.org/blog/13878/web-push-for-web-apps-on-ios-and-ipados/ (iOS 16.4 requirements) · https://webkit.org/blog/12945/meet-web-push/ (macOS) · https://webkit.org/blog/16993/news-from-wwdc25-web-technology-coming-this-fall-in-safari-26-beta/ (iOS 26 "open as web app" default)
- https://webkit.org/blog/16535/meet-declarative-web-push/
- https://caniuse.com/mdn-api_navigator_setappbadge · https://developer.mozilla.org/en-US/docs/Web/API/Badging_API
- EU reversal coverage: https://open-web-advocacy.org/blog/the-digital-markets-act-is-delivering-real-wins-but-not-yet-for-browser-engines/

**Implication for the plan:** Web Push (VAPID, own keys per server — fully self-hostable, no publisher credentials) should be the baseline notification channel for browsers, Android and installed iOS web apps; the plan needs an explicit in-app "install to Home Screen, then enable notifications" flow for iOS and must not assume badges on Android. Design the server payload once so it satisfies both the Angular service worker format and Declarative Web Push.

---

## 8. Capacitor

### Version and requirements
- **Capacitor 8** is the current major: `@capacitor/core` **8.6.2** (npm, 2026-11-12); v8.0 released 2025-12-08; MIT. Support policy: v8 Active; v7 in extended support until 2026-12-08; ≤ v6 EOL.
- **Capacitor 8 requirements:** Node ≥ 22; **Xcode 26+**; iOS deployment target **15.0**; Android Studio Otter 2025.2.1+; Android **minSdk 24, compileSdk/targetSdk 36**; AGP 8.13, Gradle 8.14.3; **Swift Package Manager is the default** for new iOS projects (CocoaPods optional). Edge-to-edge handled by the new System Bars core plugin + CSS `env()` insets.
- **Capacitor 9 is imminent:** `9.0.0-rc.0` published 2026-11-12; Ionic's roadmap targets end of November 2026. Pre-release docs list Node ≥ 24, **Xcode 27+**, iOS **16.0**, Android Studio 2026.1.4+, **minSdk 26, compile/targetSdk 37**, AGP 9.4 / Gradle 9.8, SwiftUI `App` entry point (no storyboard), R8 on by default — and, relevant here, **apps targeting API 37 need the `ACCESS_LOCAL_NETWORK` permission to reach local-network devices** (i.e. a self-hosted server on the LAN). Numbers are from unreleased docs and may still move.
- Ionic stopped selling its commercial add-ons (Identity Vault, Secure Storage, Auth Connect, Appflow) in February 2025 (Secure Storage/Appflow sunset 2027-12-31). Capacitor itself and the official plugins remain MIT and actively released. Do not plan on the Ionic enterprise plugins.

### Notifications
- **`@capacitor/push-notifications` 8.1.3** (2026-10-02, MIT): Android = FCM (bundles Firebase Messaging; needs the publisher's `google-services.json`; runtime permission on Android 13+; channels from API 26). iOS = **APNs token** by default (needs the Push capability, an APNs key/cert tied to the publisher's bundle ID, and two AppDelegate forwards). Silent/data push is not supported on iOS and data-only messages do not reach JS when the Android app is killed — rich handling needs native code or `@capacitor-firebase/messaging` (8.6.0, Apache-2.0, gives FCM tokens on both platforms).
- **`@capacitor/local-notifications` 8.3.1** (2026-08-19, MIT): schedule/show notifications locally with channels, actions, exact alarms. Useful for the "app holds its own connection and raises the notification itself" pattern (§9), but it does not wake a killed app.
- Web Push/service workers do not work inside the Capacitor WebView; the PWA and native builds therefore need two delivery paths behind one abstraction.

### Secure storage (community plugins — verified on npm)
| Plugin | Version (date) | Licence | Notes |
|---|---|---|---|
| **`@aparajita/capacitor-secure-storage`** | 8.0.1 (2026-09-23) | MIT | iOS Keychain / Android Keystore-encrypted storage; ~590 k downloads/week; tracks Capacitor majors. **Recommended.** |
| `capacitor-secure-storage-plugin` | 0.14.2 (2026-11-14) | MIT | Long-lived, still 0.x, Capacitor 8 compatible; ~262 k/week. Viable alternative. |
| Capawesome Secure Preferences | 0.3.x | Sponsorware (private registry) | Not on public npm → unsuitable for an OSS build that users compile themselves. |
| `@capgo/capacitor-native-biometric` 8.9.3 (MPL-2.0), `@aparajita/capacitor-biometric-auth` 10.0.0 (MIT) | | | Optional biometric gate in front of stored credentials. |
| `@capacitor/preferences` 8.0.1 | | MIT | **Not secure** (UserDefaults/SharedPreferences) — fine for the server URL and language, not for tokens. |

### HTTP, cookies, CORS with a user-configurable server
- App origin inside the WebView: **iOS `capacitor://localhost`, Android `https://localhost`** (defaults; changing `server.hostname`/schemes later wipes origin-scoped storage). Every API call to the user's server is therefore **cross-origin**.
- **Cookies are the fragile option.** The server's cookie is third-party relative to the app origin: WKWebView blocks third-party cookies by default since iOS 14 (the `WKAppBoundDomains` escape hatch is a compile-time list of ≤ 10 domains — impossible for arbitrary self-hosted hostnames); Android WebView needs `SameSite=None; Secure` and third-party cookies enabled; CSRF protection must then be added back. `CapacitorCookies`/`CapacitorHttp` (both built into core, off by default) route requests natively and sidestep CORS and the cookie jar rules, but patching `fetch`/XHR has known limits (large bodies over the bridge, no streaming/SSE — **UNVERIFIED** for current SSE behaviour) and makes the app behave differently from the browser build.
- **Recommended auth for the native app: short-lived bearer access token + rotating refresh token, stored in Keychain/Keystore via the secure-storage plugin**, sent in the `Authorization` header by an Angular `HttpInterceptor`. No ambient credentials → no CSRF surface, no dependence on WebView cookie policy, works identically for any server hostname. Keep the standard WebView `fetch` (so the same code path runs in the browser).
- The browser/PWA build is same-origin with its server and can use an `HttpOnly; Secure; SameSite=Strict` session cookie (better XSS posture than tokens in web storage). Supporting both means the backend accepts cookie auth and bearer auth — plan this as an explicit backend requirement (ASP.NET Core supports multiple authentication schemes).
- **CORS on the server:** must allow origins `capacitor://localhost` and `https://localhost` (exact strings; some frameworks' origin validators reject non-http schemes — test with ASP.NET Core's CORS middleware), allow the `Authorization` and `Content-Type` headers, handle `OPTIONS` preflights, and — if bearer-only — keep `Access-Control-Allow-Credentials` off. Because these origins are identical for *every* Capacitor app, CORS provides no protection by itself; authentication must carry all the weight. Make it a default-on server setting so self-hosters do not have to discover it.

### Known problems specific to "self-hosted server URL"
- **Plain HTTP / LAN servers:** Android app origin is `https://localhost`, so `http://192.168.x.x` calls are mixed content and blocked unless cleartext + mixed content are explicitly enabled; iOS App Transport Security blocks cleartext unless exceptions (`NSAllowsLocalNetworking`/arbitrary loads) are declared. Decide in the plan: require HTTPS (recommended) or ship with cleartext exceptions.
- **Self-signed / private-CA certificates:** the WebView rejects them and offers no "accept" UI. Android can trust user-installed CAs only if the app's network security config opts in; iOS requires the CA profile to be installed *and* fully trusted in Settings. Document "use a publicly trusted certificate (e.g. Let's Encrypt via reverse proxy) or install your CA".
- **Local-network permission prompts:** iOS shows the Local Network privacy prompt (needs `NSLocalNetworkUsageDescription`); Android 17 / API 37 adds `ACCESS_LOCAL_NETWORK` (see Capacitor 9 notes).
- **Server URL bootstrap:** first-run screen must validate the URL against a small unauthenticated endpoint (e.g. `/api/meta` returning product name + API version) before storing it, and the generated API client must take its base URL at runtime (§5).
- **Version skew:** store apps update independently of servers → the API needs versioning and the app needs a "server too old/new" state.
- **Service worker absent:** no offline cache via ngsw in the WebView (assets ship in the binary instead) and no Web Push.
- **App Store review:** Apple reviewers need a reachable demo server and account for an app that is useless without a server URL.

Sources:
- https://capacitorjs.com/docs/updating/8-0 · https://capacitorjs.com/docs/getting-started/environment-setup · https://capacitorjs.com/docs/main/reference/support-policy
- https://capacitorjs.com/docs/next/updating/9-0 (pre-release) · https://www.npmjs.com/package/@capacitor/core
- https://capacitorjs.com/docs/apis/push-notifications · https://capacitorjs.com/docs/apis/local-notifications
- https://capacitorjs.com/docs/apis/http · https://capacitorjs.com/docs/apis/cookies · https://capacitorjs.com/docs/config
- https://www.npmjs.com/package/@aparajita/capacitor-secure-storage · https://www.npmjs.com/package/capacitor-secure-storage-plugin
- Ionic commercial products: https://ionic.io/blog/important-announcement-the-future-of-ionics-commercial-products

**Implication for the plan:** Design the backend auth for two modes from the start (same-origin cookie for the web app, bearer + refresh token for the Capacitor app) with default-on CORS for the two Capacitor origins, and require HTTPS with a trusted certificate for mobile use. Scaffold the mobile wrapper on Capacitor 9 if it is GA when that phase starts (otherwise 8 → 9 is an early upgrade), and add an abstraction layer for notifications because the WebView has no Web Push.

---

## 9. The self-hosted push problem

**The constraint.** APNs accepts pushes only when authenticated with a key/certificate belonging to the developer account that owns the app's bundle ID; FCM only with the service-account credentials of the Firebase project compiled into the APK (`google-services.json`). A store-distributed app therefore has exactly one legitimate sender — the publisher. Shipping those credentials to every self-hosted server would let anyone push to every install. Standard **Web Push is different**: every server generates its own VAPID key pair, the browser vendor's push service accepts any VAPID-signed request, and payloads are end-to-end encrypted (RFC 8291) — so Web Push is fully self-hostable. The problem exists only for the native (Capacitor) app.

### How comparable projects solve it

| Project | Mechanism | What the relay/third party sees | Notes |
|---|---|---|---|
| **Home Assistant** | Project-operated relay (cloud function → FCM, which also fronts APNs). Plus **"Local Push"**: the app holds a WebSocket to the server and raises notifications itself (iOS/macOS while the app is active on the home network; Android via an optional *Persistent Connection* foreground service). | Notification content transits the relay and FCM **unencrypted** ("not stored", but Google could process it). Per-device registration + daily count stored. | Rate limit 500/day per device; Local Push is exempt. F-Droid ("minimal") build has no FCM, only the persistent connection. |
| **Nextcloud** | Project-operated **push proxy** (`push-notifications.nextcloud.com`). Server encrypts the payload with the device's RSA public key and signs it; proxy verifies, signs with Nextcloud's FCM/APNs credentials and forwards. Since Nextcloud 34 (2026) the server can also send **standard Web Push**, which the Android Talk app consumes via **UnifiedPush** (e.g. Sunup distributor on Mozilla's push service). | Push token, device id, user public key, timing/volume. **Not** the content. FCM/APNs do not learn which server sent it. | Free proxy under a fair-use policy; only enterprise customers with their own branded apps may run their own proxy. |
| **Mastodon** | Server speaks **only Web Push** (RFC 8030/8291, VAPID). Native apps register a Web Push subscription whose endpoint is a **relay run by the app developer**, which forwards the still-encrypted body to APNs/FCM (`mastodon/webpush-apn-relay`, `toot-relay`). App decrypts on device. | Opaque ciphertext + device token + timing. | Cleanest design: the server has one code path for browsers and apps, and third-party apps can bring their own relay. |
| **Matrix / Element** | **Push gateway** (Sygnal — now maintained at `element-hq/sygnal`; the `matrix-org` repo was archived 2025-07) run by each *app publisher* with that app's APNs/FCM credentials. The app tells its homeserver the gateway URL; the homeserver POSTs notifications there. Android F-Droid builds support **UnifiedPush** instead. | Depends on pusher format: full event metadata, or with `event_id_only` just event/room IDs and counts (app then fetches and decrypts). | Same publisher-relay pattern, standardised in the Matrix spec so any app/gateway pair works. |
| **Bitwarden** (self-hosted) | Project-operated relay `push.bitwarden.com` (or `.eu`), authenticated with a per-installation ID/key obtained from Bitwarden. | Installation ID and last-connection timestamp; pushes are **"sync now" signals**, not vault data. | Can be disabled → apps fall back to manual/periodic sync. Shows the "content-free wake-up" variant. |
| **ntfy** | Android (official app, self-hosted server): **no FCM** — the app keeps its own WebSocket/long-poll connection in a foreground service ("instant delivery"). iOS: self-hosted server sets `upstream-base-url: https://ntfy.sh`; it forwards a **poll request** containing only the message ID and a SHA-256 of the topic URL; ntfy.sh triggers APNs; the app wakes and **fetches the real message from the self-hosted server**. Also supports browser Web Push with own VAPID keys, and acts as a **UnifiedPush distributor**. | Message ID + topic hash + timing; no content. | Without the upstream, iOS delivery "can take hours". FCM for self-hosted Android needs a self-built APK. Reference design for a privacy-preserving relay. |
| **Gotify** | Android app holds a WebSocket in a foreground service; **no official iOS app**. Can act as a UnifiedPush distributor. | Nothing (no third party). | Demonstrates the cost of "no relay at all": no iOS. |
| **Immich** | No remote push channel found (roadmap and release notes mention only local/background-task notifications and e-mail). **UNVERIFIED** — absence of evidence. | — | Not a useful precedent. |
| **UnifiedPush** | Open protocol: a *distributor* app on the phone (ntfy, NextPush, Sunup/Mozilla, Gotify, an "embedded FCM distributor"…) keeps one connection to a push server the user chooses and hands messages to registered apps. Application servers send **standard Web Push** to the endpoint URL (current Android spec AND_3.x; VAPID may be required by distributors). | Whatever push server the user picks (can be self-hosted). | **Android only in practice** (also Linux/D-Bus); no iOS because iOS permits no long-lived background connections. unifiedpush.org was returning HTTP 502 during this research, so spec details are from secondary sources — **UNVERIFIED**. |

### Patterns and trade-offs

| Pattern | Pros | Cons / privacy |
|---|---|---|
| **A. Web Push only (PWA), no native push** | Zero publisher infrastructure; each server uses its own VAPID keys; payload encrypted end-to-end; works on desktop, Android, and iOS ≥ 16.4 once installed to Home Screen. | iOS requires the Home Screen install and delivery is less dependable than APNs-native for time-critical alerts; no Web Push inside the Capacitor app; push services (Google/Mozilla/Apple) see endpoint + timing + size. |
| **B. Project-operated relay** (publisher holds FCM/APNs keys) | Works with the store apps on both platforms; reliable wake-up; one-time setup for self-hosters. | The project must run and fund a highly-available internet service forever (abuse handling, rate limits, registration of instances); it becomes a single point of failure and a metadata collector (which instance, how many alerts, when). Mitigate: **send no content** — either end-to-end-encrypted payload (Nextcloud/Mastodon) or an opaque wake-up ID that the app resolves against its own server (ntfy/Bitwarden). For a *security alerting* product, alert content must never reach the relay in clear. Needs a privacy notice/GDPR position; EU-hosted option advisable. |
| **C. UnifiedPush** | No Google, user-chosen/self-hosted push server, server side is just Web Push (shared with pattern A). | Android only; user must install and configure a distributor; small, technical audience. Good as an *additional* option, not as the answer. |
| **D. App-held connection + local notifications** (WebSocket/SSE in an Android foreground service; iOS only while app is foregrounded) | No third party at all; trivial server side (reuse the live-update channel). | Android: persistent notification + battery cost, OEM background killers; needs native code beyond stock Capacitor plugins. iOS: not possible in the background. |
| **E. User-built app** (self-hoster compiles the app with their own Firebase project/Apple developer account) | Full sovereignty, no relay. | Realistic only for organisations: Apple Developer Program (US$99/yr), signing, own distribution (TestFlight/MDM/enterprise), rebuild for every update. Keep it *possible* (documented build variables) but do not rely on it. |
| **F. Out-of-band channels** (e-mail, SMS gateway, ntfy/Gotify/Matrix/Slack/Teams webhooks) | Standard for alerting tools; no app dependency; lets users plug into what they already run. | Not "in-app"; content leaves the system to those services by the user's choice. |

**Common thread in 2026:** servers converge on **standard Web Push as the single outbound protocol**, and native apps receive it through a publisher relay that is just another Web Push endpoint (Mastodon), through UnifiedPush (Nextcloud Talk, Element), or not at all.

**Suggested shape for CSH Alert:**
1. Server implements **only Web Push** (RFC 8030/8291/8292) with per-instance VAPID keys → covers browsers, installed PWAs (incl. iOS Home Screen) and UnifiedPush with one code path. This alone is a shippable v1 with no project infrastructure.
2. Add webhook/e-mail (and optionally ntfy) notification targets early — cheap, and what admins of alerting systems expect.
3. If/when the Capacitor store app ships: a small **project-operated relay that presents itself as a Web Push endpoint** and forwards the encrypted body (or an opaque alert ID) to FCM/APNs; the app decrypts or fetches from its own server. Make the relay URL configurable and the relay open source so organisations can run pattern E.
4. Offer the Android persistent-connection mode (pattern D) only if there is demand for Google-free operation beyond UnifiedPush.

Sources:
- Home Assistant: https://companion.home-assistant.io/docs/notifications/notification-details/ · https://companion.home-assistant.io/docs/notifications/notification-local
- Nextcloud: https://github.com/nextcloud/notifications/blob/master/docs/push-v2.md · https://nextcloud.com/blog/nextclouds-push-notifications-for-ios-and-android/ · https://nextcloud.com/blog/unifiedpush-android-nextcloud-talk/ (2026-09-01)
- Mastodon: https://docs.joinmastodon.org/methods/push/ · https://github.com/mastodon/webpush-apn-relay
- Matrix: https://github.com/matrix-org/sygnal (archived, points to https://github.com/element-hq/sygnal) · https://spec.matrix.org/latest/push-gateway-api/
- Bitwarden: https://bitwarden.com/help/configure-push-relay/
- ntfy: https://docs.ntfy.sh/config/ (sections "iOS instant notifications", "Firebase (FCM)", "Web Push")
- Gotify: https://gotify.net/docs/ · UnifiedPush: https://unifiedpush.org/ · https://f-droid.org/2022/12/18/unifiedpush.html

**Implication for the plan:** Treat Web Push as the only server-side push protocol and ship v1 without any project-run infrastructure; record the publisher relay (content-blind, Web-Push-compatible, open source, self-runnable) as a prerequisite and an ongoing operating commitment of the *native app* milestone, to be decided before that milestone is promised.

---

## 10. Accessibility: standard and tooling

### Status of the standards
- **WCAG 2.2 is still the current W3C Recommendation** (published 2023-10-05; current edition dated **2024-12-12**). It is also ISO/IEC 40500:2025 (ISO adoption — **UNVERIFIED** against a primary source in this research).
- **WCAG 3.0** remains an incomplete Working Draft (latest draft + Explainer September 2026). W3C states it will take "a few years", that WCAG 3 "will not supersede WCAG 2", and that WCAG 2 "will not be deprecated for several years after WCAG 3 is finalized". Meeting WCAG 2.2 is the recommended preparation. No plan impact.
- **EN 301 549 v4.1.1 was published on 2026-09-02** and now references **WCAG 2.2 A/AA** (previous v3.2.1 referenced 2.1) and maps to the European Accessibility Act. Its citation in the EU Official Journal (which gives presumption of conformity) was still pending per the sources found — **UNVERIFIED** whether it has happened by today. Either way, "WCAG 2.2 AA" is the right target and is now also where European procurement/EAA requirements are heading.

### What is new in 2.2 (vs 2.1) at A/AA, and what it means for a dashboard

| SC | Level | Requirement | Concrete consequence here |
|---|---|---|---|
| **2.4.11 Focus Not Obscured (Minimum)** | AA | A focused element must not be entirely hidden by author-created content. | Sticky table headers, a sticky app bar/bottom nav on mobile, toasts and cookie-style banners must not cover the focused row/control → use `scroll-padding-top/bottom` equal to sticky heights; toasts must not sit on top of focused content; non-modal panels must not trap focus underneath. |
| **2.5.7 Dragging Movements** | AA | Any drag operation needs a single-pointer alternative. | Column reordering/resizing, dashboard widget rearranging, sliders, drag-to-dismiss: provide buttons/menus ("Move left/right", numeric width, "Dismiss"). Simplest: avoid drag-only interactions in v1. |
| **2.5.8 Target Size (Minimum)** | AA | Pointer targets ≥ **24 × 24 CSS px**, or undersized targets spaced so a 24 px circle centred on each does not intersect another target (exceptions: inline text links, UA-default controls, essential). | Icon-only buttons in dense table rows, sort-header buttons, pagination, checkbox hit areas, close "×", tag-remove buttons. The compact "ruled" aesthetic is compatible, but bake a 24 px minimum hit area into the button/icon-button tokens (use padding or `::after` hit-area expansion). The square level tiles are fine if non-interactive, and must be ≥ 24 px if clickable. For the mobile/Capacitor layout aim for 44 px (platform guidance, AAA 2.5.5). |
| **3.2.6 Consistent Help** | A | Help mechanisms repeated across pages appear in the same relative order. | Put the help/docs/support link in one fixed place in the shell (and in the setup wizard). |
| **3.3.7 Redundant Entry** | A | Do not make users re-enter information already provided in the same process (auto-populate or offer selection), except for security/essential cases. | The multi-step setup wizard: carry values forward (server URL, admin e-mail), prefill confirmation steps; do not ask for the same contact twice. Password confirmation is exempt. |
| **3.3.8 Accessible Authentication (Minimum)** | AA | No cognitive function test (memorise, transcribe, solve) for login unless an alternative or assistance mechanism exists. | Login and 2FA: **never block paste**, use correct `autocomplete` (`username`, `current-password`, `new-password`, `one-time-code`) so password managers work; TOTP entry must allow paste/autofill; prefer offering passkeys/WebAuthn; no puzzle CAPTCHAs. Also applies to the "enter server URL" step in the app (allow paste / QR scan). |
| (4.1.1 Parsing) | — | Removed in 2.2 (obsolete). | Nothing to do. |
| 2.4.12, 2.4.13, 3.3.9 | AAA | Focus not obscured (enhanced), Focus Appearance, Accessible Authentication (enhanced) | Not required at AA, but **2.4.13 Focus Appearance** (≥ 2 px perimeter, 3:1 contrast change) is a cheap design-system decision worth adopting — a square 2 px outline suits the visual language. |

Still-relevant 2.1 AA items for this design: 1.4.11 Non-text Contrast (3:1 for control borders and for the **five-step level colour ramp** against the background — and level must never be conveyed by colour alone → the numeral in the tile covers 1.4.1), 1.4.3 text contrast on each ramp colour, 1.4.10 Reflow (320 CSS px — data tables need a reflow/stacked strategy), 1.4.12 Text Spacing, 1.4.13 Content on Hover/Focus (tooltips), 4.1.3 Status Messages (toasts and live alert updates via `aria-live` / CDK `LiveAnnouncer`), 2.2.1/2.2.2 for auto-refreshing lists (pause/control), 1.3.5 Identify Input Purpose.

### Automated tooling (current versions)

| Layer | Tool | Version (npm) | Notes |
|---|---|---|---|
| Lint (templates) | **angular-eslint** | 22.7.0 (2026-11-15), ESLint 9/10 flat config | Accessibility template rules: `alt-text`, `click-events-have-key-events`, `elements-content`, `interactive-supports-focus`, `label-has-associated-control`, `mouse-events-have-key-events`, `no-autofocus`, `no-distracting-elements`, `role-has-required-aria`, `table-scope`, `valid-aria`; enable via the `templateAccessibility` preset of the `angular-eslint` package (preset name from prior knowledge — the README only says "accessibility preset": **UNVERIFIED** exact identifier). |
| Unit/component | Vitest + Angular Testing Library (role-based queries) + axe | `@testing-library/angular` 19.6.0; `axe-core` 4.14.1 | Role/name queries catch missing semantics for free. In jsdom/happy-dom axe cannot evaluate colour contrast or layout — run axe in browser mode or leave it to Playwright. `vitest-axe` is stale (0.1.0, 2022); call `axe.run()` directly or write a 10-line matcher. |
| E2E | **`@axe-core/playwright`** | 4.14.0 (2026-10-15), MPL-2.0 | Standard choice. Use `new AxeBuilder({ page }).withTags(['wcag2a','wcag2aa','wcag21a','wcag21aa','wcag22aa'])`. **Note:** axe's only WCAG 2.2 rule, `target-size`, is tagged `wcag22aa` and is **disabled by default** — it must be enabled explicitly via tags. Scan each route in each state (dialog open, error state, 320 px viewport). |
| Component workshop | **Storybook 10** + `@storybook/addon-a11y` | 10.6.7 (2026-11-10); 11.0 in beta | See below. |
| CI page audit (optional) | Lighthouse 13.6 / pa11y-ci 5.0 | | Redundant with axe-in-Playwright; skip unless a score badge is wanted. |

Automated checks find only part of all WCAG failures (figures between roughly a third and a half are commonly cited — general knowledge, not re-verified here; none of 2.4.11, 2.5.7, 3.2.6, 3.3.7, 3.3.8 is machine-checkable). The plan needs a **manual test script** per release: keyboard-only pass, 200 %/400 % zoom and 320 px reflow, text-spacing bookmarklet, forced-colors mode, and screen-reader smoke tests (NVDA + Firefox/Chrome, VoiceOver + Safari on macOS and iOS, TalkBack on Android — the latter two also cover the Capacitor build).

### Storybook's Angular support today
- `@storybook/angular` 10.6.7 — supports **Angular ≥ 18 < 23**, still **Webpack 5-based** through its own builders (zoneless via an `experimentalZoneless` builder option).
- **`@storybook/angular-vite`** (new 2026-05; 10.6.7) — Vite 8 + the AnalogJS Angular Vite plugin, **Angular ≥ 21**, integrates with the **Storybook Vitest addon** (stories run as in-browser component tests; whether the a11y addon's checks run in that mode for Angular is not stated on the page — **UNVERIFIED**). Status: **preview**, "planned to be stable in Storybook 11" (11.0.0-beta.3 as of 2026-11-13). Known gaps: source snippets do not update with controls; some control-type inference.
- Verdict: Storybook is viable again for Angular but the modern path is not yet stable. For a library of ~15–20 own components, a small in-app "kitchen sink" route (all components, all states, used as the target of Playwright + axe and screenshot tests) is a zero-dependency alternative that also documents the design system.

Sources:
- https://www.w3.org/TR/WCAG22/ · https://www.w3.org/WAI/standards-guidelines/wcag/new-in-22/ · https://www.w3.org/WAI/standards-guidelines/wcag/wcag3-intro/
- EN 301 549 v4.1.1: https://www.deque.com/blog/en-301-549-v4-1-1-is-final-what-changed-what-it-means-and-what-you-should-do/ · https://nda.ie/news/en301549-published
- https://github.com/angular-eslint/angular-eslint/blob/main/packages/eslint-plugin-template/README.md
- https://github.com/dequelabs/axe-core/blob/develop/doc/rule-descriptions.md · https://www.npmjs.com/package/@axe-core/playwright
- https://storybook.js.org/docs/get-started/frameworks/angular · https://storybook.js.org/docs/get-started/frameworks/angular-vite

**Implication for the plan:** Keep WCAG 2.2 AA as the stated target (now also referenced by EN 301 549 v4.1.1) and encode 24 px targets, a visible 2 px focus ring, `scroll-padding` for sticky regions and paste-friendly authentication into the design tokens and component specs up front. Make angular-eslint a11y rules and Playwright + axe (with `wcag22aa` enabled) CI gates, and schedule manual screen-reader/keyboard passes — automation alone cannot demonstrate conformance.

---

## 11. Fonts and icons (self-hosted, no CDN calls)

### Fonts
| Family | Upstream | Licence | Packaging |
|---|---|---|---|
| **Source Sans 3** | Adobe (`adobe-fonts/source-sans`), latest upstream release **3.052R** (2023-04-04); repo not archived, low activity (font is mature) | **SIL OFL 1.1** | **`@fontsource-variable/source-sans-3` 5.3.1** (2026-10-11): variable weight axis **200–900**, upright + italic as separate variable files, subsets latin, latin-ext, greek(-ext), cyrillic(-ext), vietnamese, WOFF2. Static alternative: `@fontsource/source-sans-3`. |
| **Source Code Pro** | Adobe (`adobe-fonts/source-code-pro`), latest release 2.042R-u / 1.062R-i / 1.026R-vf (2023-04-12) | **SIL OFL 1.1** | **`@fontsource-variable/source-code-pro` 5.3.1** (2026-10-11): weight 200–900, upright + italic, same subsets. |

- Fontsource packages contain the font files and `@font-face` CSS; imported from `styles.css`/`angular.json` they are bundled by the Angular build and served from the app's own origin (and shipped inside the Capacitor binary) — **no request to Google Fonts or any CDN**. Fontsource builds come from the Google Fonts distribution of the families (metadata `source: google/fonts`), which is the same OFL-licensed design.
- OFL 1.1 permits bundling, embedding and redistribution with the software (including in a Docker image and app-store binaries), also commercially; obligations: keep the copyright/licence text with the font files (add `OFL.txt` to third-party notices), do not sell the fonts on their own, and do not reuse the reserved font name "Source" for modified versions (subsetting to WOFF2 as Fontsource does is common practice).
- Practical: load only `latin` + `latin-ext` (covers EN/DE/FR/IT); add `font-display: swap`; preload the one upright variable file; define `font-variant-numeric: tabular-nums` for table columns and identifiers; check that the level-tile numerals use lining tabular figures. A `Content-Security-Policy` with `font-src 'self'` then documents and enforces "no external font calls".

### Icons
| Set | Version (date) | Licence | Style / coverage | Angular package |
|---|---|---|---|---|
| **Lucide** | **1.75.0** (2026-11-18; 1.0 reached March 2026; daily releases) | **ISC** (portions derived from Feather: MIT) | Single consistent outline style, 24 px grid, 2 px stroke with adjustable `strokeWidth`/`absoluteStrokeWidth`; ~1,600+ icons (count **UNVERIFIED**); good security/infra vocabulary (shield*, lock, bell*, server, network, bug, siren, triangle-alert, activity…) | **`@lucide/angular` 1.75.0** — official, "standalone, signal-based, zoneless", one tree-shakable component per icon, Angular ≥ 17. (**`lucide-angular` is deprecated** — older tutorials use it.) Also `@ng-icons/lucide` 36.1.0. |
| **Tabler Icons** | **3.52.0** (2026-11-14) | **MIT** | Outline (and filled) variants, 24 px grid, 2 px stroke; the largest set (~6,000 icons, count **UNVERIFIED**) | **`@tabler/icons-angular` 3.52.0** — official but new and little used (≈3 k downloads/week; Angular ≥ 21); `@ng-icons/tabler-icons` 36.1.0. The older `angular-tabler-icons` stopped at Angular 19. |
| **Phosphor** | `@phosphor-icons/core` **2.1.1** (2024-03-29); `@phosphor-icons/web` 2.1.2 (2025-03) | **MIT** | Six weights (thin → fill, duotone), ~1,500 icons (count **UNVERIFIED**) | No first-party Angular package; `@ng-icons/phosphor-icons` 36.1.0. **Core set has not had a release since early 2024** — maintenance has slowed. |
| Material Symbols | `@material-symbols/svg-400` 0.49.0 | Apache-2.0 | Very broad, but carries the Material look | — (not a fit for the custom design) |
| **ng-icons** (loader, not a set) | `@ng-icons/core` 36.1.0 (2026-09-25) | MIT | One `<ng-icon>` API over many sets; requires **Angular ≥ 22** | Useful if icons from two sets must be mixed. |

**Recommendation: Lucide via `@lucide/angular`.** Most actively maintained, permissive licence, one coherent stroke style whose weight can be tuned to sit well next to Source Sans 3 (e.g. 1.5–1.75 px at 16–20 px sizes, square caps/joins if desired via CSS `stroke-linecap`/`stroke-linejoin`), first-party signal-based Angular package, icons compiled into the bundle as inline SVG (no sprite/CDN request). Fall back to Tabler only if a needed glyph is missing; avoid mixing sets. Pin the version (Lucide occasionally renames icons) and import icons individually.

Accessibility: decorative icons `aria-hidden="true"`; icon-only buttons need an accessible name (`aria-label` or visually hidden text) and a ≥ 24 px target (§10); do not rely on an icon or colour alone to convey severity.

Sources:
- https://github.com/adobe-fonts/source-sans/releases · https://github.com/adobe-fonts/source-code-pro/releases · https://openfontlicense.org/
- https://www.npmjs.com/package/@fontsource-variable/source-sans-3 · https://www.npmjs.com/package/@fontsource-variable/source-code-pro · https://fontsource.org/fonts/source-sans-3
- https://lucide.dev/guide/packages/lucide-angular · https://www.npmjs.com/package/@lucide/angular · https://www.npmjs.com/package/lucide-angular (deprecation notice)
- https://www.npmjs.com/package/@tabler/icons-angular · https://github.com/tabler/tabler-icons · https://www.npmjs.com/package/@phosphor-icons/core · https://www.npmjs.com/package/@ng-icons/core

**Implication for the plan:** Depend on `@fontsource-variable/source-sans-3`, `@fontsource-variable/source-code-pro` (latin + latin-ext only) and `@lucide/angular`, ship the OFL/ISC notices in a third-party licence file, and add a CSP (`font-src 'self'`, `img-src 'self' data:`) so "no CDN calls" is enforced rather than assumed.

---

## 12. E2E and component testing

### E2E: Playwright vs Cypress
- **Playwright** `@playwright/test` **1.65.1** (2026-11-13), Apache-2.0, ~135 M downloads/week. **Cypress 16.3.0** (2026-11-10), MIT, ~7 M/week.
- The Angular CLI is neutral: `ng e2e` offers Playwright, Cypress, Nightwatch, WebdriverIO and Puppeteer with no default (`ng add playwright-ng-schematics`, 22.1.4, tracks Angular majors).
- **Recommendation: Playwright.** Reasons specific to this project: real WebKit engine for approximating iOS Safari behaviour; free parallelism and sharding in CI; multi-context/multi-tab tests (two users, alert acknowledged in one session appears in the other); service-worker and permission control (`context.grantPermissions(['notifications'])`) for the PWA/push flows; network mocking and HAR replay against the OpenAPI contract; trace viewer for CI failures; mobile viewport/device emulation for responsive checks; first-class `@axe-core/playwright` (§10); and it is the same browser provider Angular's Vitest browser mode uses, so one browser install serves both. Cypress remains fine (and now supports Angular 21/22 zoneless component tests since v16) but its component-testing path still requires the legacy `@angular-devkit/build-angular` package, and parallelisation is tied to its paid cloud.
- Push itself cannot be delivered end-to-end in CI (needs real push services); test the subscription API and the service worker's handling by dispatching a synthetic push through CDP in Chromium, and keep a short manual device matrix (iOS Home Screen web app, Android Chrome, desktop) for real delivery.

### Unit/component testing with Vitest (Angular 22 default)
- `ng test` runs **Vitest** (`@angular/build` peers `vitest ^4.0.8 || ^5.0.0`; Vitest **5.1.1** is current) through the `application` builder. Default environment is DOM emulation — **happy-dom if installed, otherwise jsdom** (jsdom 31.0.1). Custom `vitest.config` contents are "not directly supported" by the Angular team and the CLI overrides `test.include`/`test.projects`.
- **Browser mode** (real Chromium/Firefox/WebKit) is supported by installing a provider — `@vitest/browser-playwright` 5.1.1 or `@vitest/browser-webdriverio` — and listing `browsers` in the test target; headless automatically when `CI` is set. Use it for the component library (focus management, overlays, CSS-dependent behaviour, axe contrast checks); keep jsdom/happy-dom for fast logic and service tests.
- Component test style options:
  1. **TestBed + `ComponentFixture`** with `await fixture.whenStable()` (zoneless-friendly; avoid `fakeAsync`, which needs the zone.js Vitest patch) — baseline, no extra dependency.
  2. **Angular Testing Library** (`@testing-library/angular` 19.6.0, Angular ≥ 21) + `@testing-library/user-event` 14.6 — role/label queries and realistic interaction; recommended for the UI components because the tests double as accessibility assertions.
  3. **Component harnesses** — CDK `ComponentHarness` and the new **Angular Aria harnesses** (`@angular/aria/*/testing`) for driving Aria-based widgets (combobox, tabs, tree) without depending on DOM details; write harnesses for own complex components.
  4. **Storybook Vitest addon** via `@storybook/angular-vite` (preview) — stories as browser tests; only if Storybook is adopted (§10).
  5. Playwright against a "kitchen sink" route — covers visual regression (`toHaveScreenshot`) and axe for every component state.
- HTTP in tests: `provideHttpClientTesting()`/`HttpTestingController` works with `httpResource` (it goes through `HttpClient`); MSW 3.0 is an option for browser-level mocking shared between Vitest browser mode, Storybook and Playwright.
- Legacy to avoid: Karma/Jasmine, Protractor, `fakeAsync`/`waitForAsync`, `NO_ERRORS_SCHEMA`, Jest (never became an officially stable Angular runner).

Sources:
- https://angular.dev/guide/testing · https://angular.dev/guide/testing/migrating-to-vitest · https://angular.dev/tools/cli/end-to-end
- https://www.npmjs.com/package/@playwright/test · https://www.npmjs.com/package/playwright-ng-schematics · https://www.npmjs.com/package/cypress
- https://docs.cypress.io/app/component-testing/angular/overview
- https://www.npmjs.com/package/vitest · https://www.npmjs.com/package/@vitest/browser-playwright · https://www.npmjs.com/package/@testing-library/angular

**Implication for the plan:** Standardise on Vitest (CLI default; jsdom/happy-dom for logic, Playwright-provider browser mode for UI components, Angular Testing Library queries) and Playwright for E2E + axe + screenshots, with one shared Playwright browser install in CI. Do not introduce Karma, Jest or Cypress.

---

## Cross-cutting notes

- **Things that changed in the last 12 months and invalidate older advice:** Angular moved to yearly majors (v22 current, TS 6.0 required, TS 7 not yet supported); Signal Forms, `resource`/`httpResource` and Angular Aria are stable; PrimeNG ≥ 22 is commercial/closed; spartan/ui is 1.x; TanStack Query's Angular package left "experimental" on 2026-11-17; ngx-translate was revived (v18, signals); Lucide is 1.x with a new `@lucide/angular` package; Capacitor 9 RC is out (Node 24, Xcode 27, API 37); EN 301 549 v4.1.1 now references WCAG 2.2; Nextcloud added standard Web Push/UnifiedPush; hey-api and openapi-fetch have lost momentum.
- **Dependency hygiene:** everything recommended above is MIT/Apache-2.0/ISC/OFL/MPL-2.0 (axe-core is MPL-2.0 — dev-only). No recommended runtime dependency requires a licence key or a non-public registry.

## UNVERIFIED items (consolidated)

1. Angular 22.2 "router resources" and `@boundary` being in developer preview — from secondary coverage only.
2. Intended removal version of `@angular/animations` (deprecated since 20.2 is confirmed; removal version is not stated on angular.dev).
3. Absence of a Tailwind CSS v5 announcement (nothing found).
4. NgRx Events plugin's current stability label in v22.
5. OpenAPI Generator `typescript-angular` completeness for OpenAPI 3.1 edge cases; ASP.NET Core's default OpenAPI version for the chosen .NET release (backend research should confirm).
6. Reason for hey-api's release pause since 2026-06-22 (observed from GitHub/npm; no statement found).
7. First-party type-safe translation keys in Transloco 9 / ngx-translate 18 (none found).
8. Current (late-2026) status of iOS Home Screen web apps in the EU beyond Apple's March 2024 reversal; and whether iOS 27 changed anything for web apps (nothing found).
9. Declarative Web Push: exact W3C standardisation stage; non-shipment in Chrome/Firefox (no primary source either way).
10. CapacitorHttp behaviour with streaming/SSE in Capacitor 8; all Capacitor 9 numbers (taken from pre-release docs).
11. UnifiedPush spec details (unifiedpush.org returned HTTP 502 during research); Mastodon official apps' relay endpoint; Immich having no remote push.
12. ISO/IEC 40500:2025 adoption of WCAG 2.2; whether EN 301 549 v4.1.1 has been cited in the EU Official Journal yet.
13. Exact identifier of the angular-eslint accessibility preset (`templateAccessibility`).
14. Icon counts for Lucide/Tabler/Phosphor.
15. Storybook a11y addon behaviour inside the Vitest addon for `@storybook/angular-vite` (documented for other frameworks; not stated on the Angular page).
