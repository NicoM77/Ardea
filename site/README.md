# Website

The product page and the documentation of Ardea, built with [Astro](https://astro.build) and
[Starlight](https://starlight.astro.build) (both MIT). Node 24.

```bash
npm ci
npm run build          # docs/ into the site, then astro build into dist/
npm run preview        # dist/ on http://127.0.0.1:4330/Ardea/
npm run check:links    # every internal link and anchor in dist/
npm run check:a11y     # axe on every page except most API operations (Playwright)
npm run check:licenses # production packages against build/licenses/allowed-licenses.json
npm run dev            # live reload; run it again after a change in docs/
```

## Where the content comes from

Nothing in `site/` is a copy of the documentation. `scripts/sync-docs.mjs` runs before every build and writes the
pages into `src/content/docs/` (ignored by Git) from their single source:

| Source | Page |
| --- | --- |
| `docs/operations/*.md` | Run Ardea: overview, try it, installation, configuration, operation, update, add a source |
| `docs/architecture.md`, `docs/architecture/*.md` | Architecture: overview, data model, rule set and levels |
| `docs/adr/README.md`, `docs/adr/0*.md` | Decision records |
| `SECURITY.md`, `docs/security/security-model.md`, `docs/security/threat-review.md` | Security: policy, model, threat review |
| `CONTRIBUTING.md` | Contributing |
| `openapi/ardea.v1.json` | API reference ([starlight-openapi](https://github.com/HiDeoo/starlight-openapi), MIT) |
| `README.md` | Purpose, features and quick start on the landing page (`src/readme.ts`) |
| `docs/design/brand/`, `docs/screenshots/` | Logo, favicon and screenshots, copied into `public/media/` (ignored by Git) |

The first `# heading` of a Markdown file becomes the page title. Relative links are rewritten: to the page when the
target is one of the files above, otherwise to the file on GitHub. A link to a file that does not exist fails the
build. `docs/masterplan/` and `docs/acceptance/` are not part of the site; links to them go to GitHub.

The design follows `DESIGN.md`: the colour and type tokens are `web/projects/ui/src/styles/tokens.css` itself, IBM Plex
comes from Fontsource, and the site is light only.

## Address

`site.config.mjs` holds the address: `https://nicom77.github.io/Ardea/` by default, `ARDEA_SITE_URL` and
`ARDEA_SITE_BASE` change it (for example `ARDEA_SITE_BASE=/` for a custom domain).

## Publishing

`.github/workflows/site.yml` builds, checks and publishes the site to GitHub Pages (Settings → Pages → Source
«GitHub Actions») on every published release, on every push to `main` that changes `docs/**`, `README.md`,
`SECURITY.md`, `CONTRIBUTING.md`, `openapi/**` or `site/**`, and when started by hand. The Publish job runs only for
`main` and releases: `gh workflow run site.yml --ref <branch>` builds and checks a branch without publishing it.
A change to the workflow file alone starts nothing, so start it by hand on `main` after one.

## Accessibility check locally

The axe check runs in the Playwright image the web app uses, after `npm ci` and `npm run build`:

```bash
MSYS_NO_PATHCONV=1 docker run --rm --ipc=host -e HOME=/tmp -e CI=true -v "$PWD/..":/repo -w /repo/site \
  mcr.microsoft.com/playwright:v1.56.1-noble npx playwright test
```
