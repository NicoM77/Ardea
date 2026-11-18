import { defineConfig, devices } from '@playwright/test';

/**
 * Visual comparison of the component library pages (master plan 13.6): one image per page,
 * Chromium on Linux, under a minute. It runs against a development build (the production build
 * leaves the /ui pages out), served by scripts/serve-static.mjs. Build first, then compare in the
 * official image so the baselines match CI:
 *
 *   npx ng build ardea --configuration development
 *   docker run --rm --ipc=host --user "$(id -u):$(id -g)" -e HOME=/tmp \
 *     -v "$PWD":/w -w /w mcr.microsoft.com/playwright:v1.56.1-noble npx playwright test
 *
 * Add `--update-snapshots=all` after an intended visual change and commit the new images.
 */
const port = 4300;
const isCi = !!process.env['CI'];

export default defineConfig({
  testDir: './e2e',
  snapshotPathTemplate: '{testDir}/baselines/{arg}{ext}',
  fullyParallel: true,
  forbidOnly: isCi,
  retries: 0,
  reporter: isCi ? [['github'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: `http://127.0.0.1:${port}`,
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
      name: 'chromium',
      use: {
        ...devices['Desktop Chrome'],
        viewport: { width: 1440, height: 900 },
        deviceScaleFactor: 1,
      },
    },
  ],
  webServer: {
    command: `node scripts/serve-static.mjs dist/ardea/browser ${port}`,
    url: `http://127.0.0.1:${port}/ui`,
    reuseExistingServer: !isCi,
    timeout: 30_000,
  },
});
