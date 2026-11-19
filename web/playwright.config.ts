import { defineConfig, devices } from '@playwright/test';
import { existsSync } from 'node:fs';

/**
 * Three Playwright projects, all in Chromium on Linux, all served by scripts/serve-static.mjs with
 * the production Content Security Policy:
 *
 * - `visual`: one image per component library page (master plan 13.6), against a development build
 *   in dist/showcase (the production build leaves the /ui pages out). Main branch only in CI.
 * - `csp`: the production build in dist/ardea loads its pages under the policy without a single
 *   console error. Runs on every pull request.
 * - `flows`: the screens of phase 3 (overview, actions, action, alert) in the same build against a
 *   stateful stand-in for the API (e2e/flows-api.ts), with axe (WCAG 2.2 AA) on every page and the
 *   layout check of the three answers at 390 × 844. Runs on every pull request with `csp`.
 *
 * Build, then run in the official image so the images match CI (the repository is mounted whole,
 * because the server reads the policy from the host's source):
 *
 *   npx ng build ardea && npx ng build ardea --configuration development --output-path dist/showcase
 *   docker run --rm --ipc=host --user "$(id -u):$(id -g)" -e HOME=/tmp -e CI=true \
 *     -v "$PWD/..":/repo -w /repo/web mcr.microsoft.com/playwright:v1.56.1-noble npx playwright test
 *
 * Add `--update-snapshots=all` after an intended visual change and commit the new images.
 */
const isCi = !!process.env['CI'];
const showcase = { dir: 'dist/showcase/browser', port: 4300 };
const production = { dir: 'dist/ardea/browser', port: 4301 };

/** Only the builds that exist are served, so either project can run on its own. */
const servers = [showcase, production]
  .filter((build) => existsSync(`${build.dir}/index.html`))
  .map((build) => ({
    command: `node scripts/serve-static.mjs ${build.dir} ${build.port}`,
    url: `http://127.0.0.1:${build.port}/`,
    reuseExistingServer: !isCi,
    timeout: 30_000,
  }));

export default defineConfig({
  testDir: './e2e',
  snapshotPathTemplate: '{testDir}/baselines/{arg}{ext}',
  fullyParallel: true,
  forbidOnly: isCi,
  retries: 0,
  reporter: isCi ? [['github'], ['html', { open: 'never' }]] : 'list',
  use: {
    trace: 'retain-on-failure',
  },
  expect: {
    toHaveScreenshot: {
      animations: 'disabled',
      caret: 'hide',
      scale: 'css',
      // The palette is made of near greys (Surface #FFF against Raised #F5F7F8), which the default
      // per-pixel threshold of 0.2 would not see. The image renders the same in every run, so be strict.
      threshold: 0.02,
      maxDiffPixels: 20,
    },
  },
  projects: [
    {
      name: 'visual',
      testMatch: 'visual.spec.ts',
      use: {
        ...devices['Desktop Chrome'],
        baseURL: `http://127.0.0.1:${showcase.port}`,
        viewport: { width: 1440, height: 900 },
        deviceScaleFactor: 1,
        // The loader's loop would be cut to its first, empty frame; reduced motion shows the mark.
        reducedMotion: 'reduce',
      },
    },
    {
      name: 'csp',
      testMatch: 'csp.spec.ts',
      use: {
        ...devices['Desktop Chrome'],
        baseURL: `http://127.0.0.1:${production.port}`,
      },
    },
    {
      // The screens of phase 3 with axe, against a stand-in for the API (e2e/flows-api.ts).
      name: 'flows',
      testMatch: 'flows.spec.ts',
      use: {
        ...devices['Desktop Chrome'],
        baseURL: `http://127.0.0.1:${production.port}`,
        viewport: { width: 1440, height: 900 },
      },
    },
  ],
  webServer: servers,
});
