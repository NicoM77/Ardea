import { expect, test, type Browser, type Page } from '@playwright/test';
import path from 'node:path';
import { openFirstPage, watchConsole } from '../support';

const ORG = '/o/example-digital';
const OUT = path.resolve(__dirname, '../../../docs/screenshots');
const email = process.env['SCREENSHOTS_EMAIL'] ?? '';
const password = process.env['SCREENSHOTS_PASSWORD'] ?? '';
const seededAt = Number(process.env['SCREENSHOTS_SEEDED_AT'] ?? Date.now());
const clock = new Date(seededAt + 2 * 60 * 60_000);
const PHONE = { width: 390, height: 844 };

async function settle(page: Page): Promise<void> {
  await page.waitForLoadState('load');
  await expect(page.locator('ui-loader')).toHaveCount(0);
  await expect(page.locator('[aria-busy="true"]')).toHaveCount(0);
  await page.evaluate(async () => {
    await document.fonts.ready;
    await Promise.all([...document.images].map((img) => img.decode().catch(() => undefined)));
  });
  await page.mouse.move(0, 0);
  await page.waitForTimeout(500);
}

async function capture(page: Page, name: string): Promise<void> {
  await settle(page);
  await page.screenshot({
    path: path.join(OUT, `${name}.png`),
    animations: 'disabled',
    caret: 'hide',
  });
}

async function phone(browser: Browser, page: Page): Promise<Page> {
  const context = await browser.newContext({
    storageState: await page.context().storageState(),
    viewport: PHONE,
    deviceScaleFactor: 2,
    isMobile: true,
    hasTouch: true,
    colorScheme: 'light',
    reducedMotion: 'reduce',
    locale: 'en-GB',
    timezoneId: 'Europe/Zurich',
  });
  const small = await context.newPage();
  await small.clock.setFixedTime(clock);
  return small;
}

test('screenshots of the demo instance', async ({ page, browser }) => {
  test.skip(!email || !password, 'Run through build/screenshots.sh');
  const problems = watchConsole(page);
  await page.clock.setFixedTime(clock);

  await openFirstPage(page, '/sign-in');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`${ORG}/overview$`));
  await expect(page.getByRole('heading', { level: 1, name: 'Overview' })).toBeVisible();
  await capture(page, 'overview');

  await page.goto(`${ORG}/notices`);
  await expect(page.getByRole('heading', { level: 1, name: 'Notices' })).toBeVisible();
  await capture(page, 'notices');

  await page.goto(`${ORG}/reminders`);
  await expect(page.getByRole('heading', { level: 1, name: 'Reminders' })).toBeVisible();
  await settle(page);
  await page
    .locator('main')
    .getByRole('link', { name: /^(Update|Mitigate) / })
    .first()
    .click();
  await expect(page).toHaveURL(new RegExp(`${ORG}/reminders/\\d+$`));
  const reminder = new URL(page.url()).pathname;
  await capture(page, 'reminder');

  await page.goto('/entries/CVE-2026-109619');
  await expect(page.getByRole('heading', { level: 1 })).toContainText('CVE-2026-109619');
  await capture(page, 'vulnerability');

  await page.goto(`${ORG}/setup?step=stack`);
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  await capture(page, 'wizard-stack');

  const small = await phone(browser, page);
  const smallProblems = watchConsole(small);
  await small.goto(`${ORG}/overview`);
  await expect(small.getByRole('heading', { level: 1, name: 'Overview' })).toBeVisible();
  await capture(small, 'phone-overview');
  await small.goto(reminder);
  await capture(small, 'phone-reminder');

  expect([...problems, ...smallProblems]).toEqual([]);
});
