import { defineConfig, devices } from '@playwright/test';

/**
 * End-to-end tests of the core flows (master plan 13.6) against a real instance: the image
 * started with deploy/compose.yaml and deploy/compose.caddy.yaml, HTTPS from Caddy's local
 * authority. run.sh starts the instance and passes its address and setup token.
 */
const isCi = !!process.env['CI'];

export default defineConfig({
  testDir: '.',
  testMatch: '*.spec.ts',
  // The flows build on one instance in order (the first user is created once).
  fullyParallel: false,
  workers: 1,
  forbidOnly: isCi,
  retries: 0,
  timeout: 120_000,
  reporter: isCi ? [['github'], ['html', { open: 'never' }]] : 'list',
  use: {
    ...devices['Desktop Chrome'],
    baseURL: process.env['E2E_BASE_URL'] ?? 'https://localhost',
    viewport: { width: 1440, height: 900 },
    // Caddy's internal authority issues the certificate for localhost.
    ignoreHTTPSErrors: true,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
});
