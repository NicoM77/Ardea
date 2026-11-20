import { defineConfig, devices } from '@playwright/test';

const isCi = !!process.env['CI'];

export default defineConfig({
  testDir: '.',
  testMatch: '*.spec.ts',
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
    ignoreHTTPSErrors: true,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
});
