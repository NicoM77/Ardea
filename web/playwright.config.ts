import { defineConfig, devices } from '@playwright/test';
import { existsSync } from 'node:fs';

const isCi = !!process.env['CI'];
const showcase = { dir: 'dist/showcase/browser', port: 4300 };
const production = { dir: 'dist/ardea/browser', port: 4301 };

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
