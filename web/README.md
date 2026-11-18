# Ardea web

Angular 22 workspace: the application `ardea` (`projects/ardea`) and the component library `ui`
(`projects/ui`). Standalone components, signals, zoneless change detection, OnPush (the Angular 22
default), strict TypeScript. Rules: master plan chapters 2.4, 11 and 13.4; the look: `../DESIGN.md`.

Requirements: Node 24 (`fnm use 24`).

```bash
npm ci
npm start                # dev server on http://localhost:4200, /api proxied to http://localhost:5080
npm test                 # Vitest unit tests (ui and ardea)
npm run lint             # ESLint with angular-eslint, template accessibility rules included
npm run check:i18n       # every key used in code is in en.json, and every key in en.json is used
npm run check:tokens     # no colour values outside projects/ui/src/styles/tokens.css
npm run check:licenses   # production packages against ../build/licenses/allowed-licenses.json
npm run build            # production build into dist/ardea/browser (base href "/", hashed files)
```

## Layout

| Where | What |
|---|---|
| `projects/ui/src/styles/` | `tokens.css` (the only place with colour values), base, controls, overlay, motion, fonts |
| `projects/ui/src/lib/` | the components; each has its states (default, hover, focus, disabled, loading, error, empty) |
| `projects/ardea/src/app/core/` | API services, session and guards, HTTP interceptor, translations |
| `projects/ardea/src/app/layout/` | the signed-in shell (sidebar, top bar, search) and the sign-in frame |
| `projects/ardea/src/app/features/` | pages: sign-in area, account, placeholder areas |
| `projects/ardea/src/app/showcase/` | the component library pages at `/ui`, development builds only |
| `projects/ardea/src/i18n/en.json` | English, built in; other languages load from `/i18n/<lang>.json` |
| `e2e/` | visual comparison of the `/ui` pages, baselines in `e2e/baselines/` |

The API services in `core/api/` are written by hand and kept thin; task 0.11 replaces them with a
client generated from the OpenAPI document.

## Visual comparison

One image per component library page, compared in Chromium on Linux against a development build
(the production build leaves `/ui` out). Build on the host, then compare in the official
Playwright image so the result matches CI:

```bash
npx ng build ardea --configuration development
docker run --rm --ipc=host --user "$(id -u):$(id -g)" -e HOME=/tmp -e CI=true \
  -v "$PWD":/w -w /w mcr.microsoft.com/playwright:v1.56.1-noble npx playwright test
```

After an intended change, add `--update-snapshots=all` and commit the new images. The comparison
is strict (near-grey palette), so review the diff images in `test-results/` before accepting.
CI runs it on pushes to `main` only (master plan 13.7).

## Licence exceptions

`license-exceptions.json` names each production package whose licence is not on the allow-list,
with the reason it is accepted. The check fails on any other package and on a stale entry.
