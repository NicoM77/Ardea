import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './screenshots',
  testMatch: '*.capture.ts',
  outputDir: 'test-results/screenshots',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 120_000,
  reporter: 'list',
  use: {
    ...devices['Desktop Chrome'],
    baseURL: 'http://localhost:8080',
    viewport: { width: 1440, height: 900 },
    deviceScaleFactor: 2,
    colorScheme: 'light',
    reducedMotion: 'reduce',
    locale: 'en-GB',
    timezoneId: 'Europe/Zurich',
    trace: 'retain-on-failure',
  },
});
