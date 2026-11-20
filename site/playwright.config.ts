import { defineConfig, devices } from '@playwright/test';
import { base } from './site.config.mjs';

const isCi = !!process.env['CI'];
const port = 4330;

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: isCi,
  retries: 0,
  reporter: isCi ? [['github'], ['list']] : 'list',
  webServer: {
    command: `node scripts/serve.mjs ${port}`,
    url: `http://127.0.0.1:${port}${base}/`,
    reuseExistingServer: !isCi,
    timeout: 30_000,
  },
  use: {
    ...devices['Desktop Chrome'],
    baseURL: `http://127.0.0.1:${port}`,
    reducedMotion: 'reduce',
  },
});
