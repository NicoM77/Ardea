import { expect, test } from '@playwright/test';
import { watchConsole } from './console';

const PAGES = [
  'foundations',
  'buttons',
  'actions',
  'fields',
  'feedback',
  'signals',
  'two-factor',
  'shell',
];

for (const name of PAGES) {
  test(`component library: ${name}`, async ({ page }) => {
    const problems = watchConsole(page);
    await page.route('**/api/**', (route) =>
      route.request().url().endsWith('/api/v1/setup/status')
        ? route.fulfill({ json: { setupRequired: false } })
        : route.fulfill({ status: 401, json: { status: 401 } }),
    );

    await page.goto(`/ui/${name}`);
    await expect(page.locator('h1').first()).toBeVisible();
    await expect(page.locator('ui-loader:not(.is-visible)')).toHaveCount(0);
    await page.evaluate(async () => {
      await document.fonts.ready;
      await Promise.all([...document.images].map((img) => img.decode().catch(() => undefined)));
    });
    await expect(page).toHaveScreenshot(`${name}.png`, { fullPage: true });
    expect(problems).toEqual([]);
  });
}
