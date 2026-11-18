import { expect, test } from '@playwright/test';

/** The component library pages (/ui, development builds only). One full-page image each. */
const PAGES = ['foundations', 'buttons', 'fields', 'feedback', 'signals', 'two-factor', 'shell'];

test.beforeEach(async ({ page }) => {
  // No backend: the app's start-up antiforgery call is answered here, nothing else reaches /api.
  await page.route('**/api/**', (route) => route.fulfill({ status: 204 }));
});

for (const name of PAGES) {
  test(`component library: ${name}`, async ({ page }) => {
    await page.goto(`/ui/${name}`);
    await expect(page.locator('h1').first()).toBeVisible();
    // Fonts and images (the logo) fully decoded, so no run captures a half-drawn frame.
    await page.evaluate(async () => {
      await document.fonts.ready;
      await Promise.all([...document.images].map((img) => img.decode().catch(() => undefined)));
    });
    await expect(page).toHaveScreenshot(`${name}.png`, { fullPage: true });
  });
}
