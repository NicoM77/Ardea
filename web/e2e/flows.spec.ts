import { expect, test } from '@playwright/test';
import { expectAccessible } from './axe';
import { watchConsole } from './console';
import { flowsApi } from './flows-api';
import { PROJECT_ID } from './mock-api';

/**
 * The core screens of phase 3 (tasks 3.8 to 3.11) in the production build under the production
 * policy, against a stateful stand-in for the API (e2e/flows-api.ts): the overview on a busy and a
 * quiet day, the filters of the actions list, a status change with the 409 conflict and taking a
 * closing back, the three answers above the action bar at 390 × 844, and the alert. Phase 4 adds
 * the model's assessment: its texts as plain text, the proposal confirmed and a verdict corrected
 * (task 4.7), and a request for an assessment refused for today (task 4.3). Every page is checked
 * with axe (WCAG 2.2 AA) and must log no console error.
 */
const ORG = '/o/example';

test('overview on a busy day: the finding, deadlines, the urgent answers and the warnings', async ({
  page,
}) => {
  const problems = watchConsole(page);
  await flowsApi(page);
  await page.goto(`${ORG}/overview`);

  await expect(page.getByRole('heading', { level: 1, name: 'Overview' })).toBeVisible();
  await expect(page.getByRole('status').filter({ hasText: 'critical action is open' })).toHaveText(
    /1 critical action is open\. 0 due today, 1 without an owner\./,
  );
  const deadlines = page.getByRole('region', { name: 'Deadlines' });
  await expect(deadlines.getByRole('link')).toHaveCount(5);
  await expect(deadlines.getByRole('link').nth(1)).toContainText(
    'Update Puppet Enterprise to 2025.12.1',
  );
  const urgent = page.getByRole('region', { name: 'Most urgent' });
  await expect(urgent.getByRole('heading', { name: 'What happened' })).toBeVisible();
  await expect(urgent.getByRole('heading', { name: 'Are we affected?' })).toBeVisible();
  await expect(urgent.getByRole('heading', { name: 'What to do' })).toBeVisible();
  const warnings = page.getByRole('region', { name: 'Warnings without an identifier' });
  await expect(warnings.getByRole('link')).toContainText(
    'Phishing campaign against hospital suppliers',
  );
  await expect(warnings).toContainText('BACS warnings');
  await expectAccessible(page, 'overview busy');
  expect(problems).toEqual([]);
});

test('overview on a quiet day keeps its cards and says nothing is urgent', async ({ page }) => {
  const problems = watchConsole(page);
  await flowsApi(page, { calm: true });
  await page.goto(`${ORG}/overview`);

  await expect(page.getByRole('status').filter({ hasText: 'Nothing urgent.' })).toContainText(
    'No critical or high action is open; the next deadline is in 28 days.',
  );
  await expect(page.getByRole('region', { name: 'Deadlines' }).getByRole('link')).toHaveCount(3);
  await expect(page.getByRole('region', { name: 'By priority' })).toContainText('2open');
  await expect(page.getByRole('region', { name: 'Sources' })).toBeVisible();
  await expectAccessible(page, 'overview calm');
  expect(problems).toEqual([]);
});

test('actions list: filter by priority and project, search, and the closed view', async ({
  page,
}) => {
  const problems = watchConsole(page);
  await flowsApi(page);
  await page.goto(`${ORG}/actions`);

  const table = page.getByRole('table', { name: 'Open actions' });
  await expect(table.getByRole('row')).toHaveCount(5);
  await expect(page.getByRole('status').filter({ hasText: 'open.' })).toHaveText(
    '4 open. 1 critical, 3 unassigned.',
  );
  await expectAccessible(page, 'actions');

  const critical = page.getByRole('button', { name: /Critical/ });
  await expect(critical).toHaveText(/Critical\s*1/);
  const levelRequest = page.waitForRequest(
    (r) => new URL(r.url()).searchParams.get('level') === '5',
  );
  await critical.click();
  await levelRequest;
  await expect(page).toHaveURL(/priority=critical/);
  await expect(critical).toHaveAttribute('aria-pressed', 'true');
  await expect(table.getByRole('row')).toHaveCount(2);
  await expect(table).toContainText('Update Puppet Enterprise to 2025.12.1');
  await critical.click();
  await expect(critical).toHaveAttribute('aria-pressed', 'false');
  await expect(page).not.toHaveURL(/priority=/);

  await page.getByRole('combobox', { name: 'Project' }).selectOption(PROJECT_ID);
  await expect(page).toHaveURL(new RegExp(`project=${PROJECT_ID}`));
  await expect(table.getByRole('row')).toHaveCount(2);
  await expect(table).toContainText('Update Umbraco CMS to 17.7.1');
  await page.getByRole('combobox', { name: 'Project' }).selectOption('');
  await expect(page).not.toHaveURL(/project=/);

  await page.getByRole('searchbox', { name: 'Search actions' }).fill('postgres');
  await page.getByRole('searchbox', { name: 'Search actions' }).press('Enter');
  await expect(page).toHaveURL(/q=postgres/);
  await expect(table.getByRole('row')).toHaveCount(2);
  await expect(page.getByRole('status').filter({ hasText: 'match' })).toHaveText(
    '1 open action matches.',
  );

  await page.goto(`${ORG}/actions`);
  await page
    .getByRole('navigation', { name: 'Show' })
    .getByRole('link', { name: 'Closed' })
    .click();
  await expect(page).toHaveURL(/view=closed/);
  const closed = page.getByRole('table', { name: 'Closed actions' });
  await expect(closed.getByRole('row')).toHaveCount(2);
  await expect(closed).toContainText('Update .NET runtime to 10.0.4');
  await expect(closed).toContainText('Remediated');
  expect(problems).toEqual([]);
});

test('action: someone else changed it meanwhile (409), the page says so and shows the current state', async ({
  page,
}) => {
  const problems = watchConsole(page);
  await flowsApi(page, { conflict: true });
  await page.goto(`${ORG}/actions/143`);
  await expect(
    page.getByRole('heading', { level: 1, name: 'Update PostgreSQL to 17.12' }),
  ).toBeVisible();

  await page.getByRole('button', { name: 'Take into review' }).click();
  await expect(page.getByRole('alert')).toHaveText(
    'Someone changed this action in the meantime. It shows the current state now; decide again.',
  );
  // Read again: someone else has it in review now.
  await expect(page.getByRole('button', { name: 'Give back' })).toBeVisible();
  await expect(page.getByLabel('Owner')).toHaveValue('0190a000-0000-7000-8000-000000000004');
  expect(problems.filter((p) => !p.includes('status of 409'))).toEqual([]);
});

test('action: take into review, close with a reason, take the closing back', async ({ page }) => {
  const problems = watchConsole(page);
  await flowsApi(page);
  await page.goto(`${ORG}/actions/143`);
  await expect(
    page.getByRole('heading', { level: 1, name: 'Update PostgreSQL to 17.12' }),
  ).toBeVisible();
  await expectAccessible(page, 'action');

  await page.getByRole('button', { name: 'Take into review' }).click();
  const toast = page.locator('ui-toast');
  await expect(toast).toContainText('In review and assigned to you');
  await expect(page.getByLabel('Owner')).toHaveValue('0190a000-0000-7000-8000-000000000001');

  await page.locator('#action-status').selectOption('remediated');
  const reason = page.getByLabel('How was it remediated?');
  await expect(reason).toBeFocused();
  await page.getByRole('button', { name: 'Close for all findings' }).click();
  await expect(page.getByText('Write a short reason; it stays in the history.')).toBeVisible();
  await reason.fill('Updated the primary and the replicas to 17.12.');
  await page.getByRole('button', { name: 'Close for all findings' }).click();
  await expect(toast).toContainText('Closed as remediated for all findings');
  await expect(page.locator('#action-status')).toHaveValue('remediated');
  await expect(page.getByRole('region', { name: 'History' })).toContainText(
    'Updated the primary and the replicas to 17.12.',
  );

  // Taking the closing back sends «open» against the status the page saw: closed.
  const reopen = page.waitForRequest(
    (r) => r.url().endsWith('/actions/143/status') && r.postDataJSON()?.expectedStatus === 'closed',
  );
  await page.getByRole('button', { name: 'Reopen' }).click();
  expect((await reopen).postDataJSON()).toMatchObject({ status: 'open', expectedStatus: 'closed' });
  await expect(toast).toContainText('Closing taken back: the action is open again');
  await expect(page.getByRole('button', { name: 'Take into review' })).toBeVisible();
  await expectAccessible(page, 'action after reopening');
  expect(problems).toEqual([]);
});

test.describe('at 390 × 844', () => {
  test.use({ viewport: { width: 390, height: 844 }, hasTouch: true });

  test('the three answers sit above the action bar', async ({ page }) => {
    const problems = watchConsole(page);
    await flowsApi(page);
    await page.goto(`${ORG}/actions/143`);
    const bar = page.locator('.ui-actionbar');
    await expect(bar).toBeVisible();
    await expect(bar.getByRole('button', { name: 'Take into review' })).toBeVisible();
    const barTop = (await bar.boundingBox())!.y;
    expect(barTop).toBeGreaterThan(700);
    for (const part of ['happened', 'affected', 'todo']) {
      const box = (await page.locator(`ardea-action-page [data-answer="${part}"]`).boundingBox())!;
      expect(box.y + box.height, `${part} ends above the action bar`).toBeLessThanOrEqual(barTop);
    }
    await expectAccessible(page, 'action on a phone');
    expect(problems).toEqual([]);
  });
});

test('alert: the components hit with their factors, and the entry', async ({ page }) => {
  const problems = watchConsole(page);
  await flowsApi(page);
  await page.goto(`${ORG}/alerts/142`);
  await expect(
    page.getByRole('heading', {
      level: 1,
      name: 'PostgreSQL: function owner’s rights through a crafted search path',
    }),
  ).toBeVisible();
  const hits = page.getByRole('table', { name: 'Components hit' });
  await expect(hits).toContainText('PostgreSQL 17.9');
  await expect(
    page.getByRole('table', { name: 'Factors for PostgreSQL in Platform Ops' }),
  ).toContainText('5 points · rule set v1');
  await expect(page.getByRole('link', { name: 'Action #143' })).toBeVisible();
  await expectAccessible(page, 'alert');
  expect(problems).toEqual([]);
});

test('action with the model’s assessment: plain text, the proposal confirmed, a verdict corrected', async ({
  page,
}) => {
  const problems = watchConsole(page);
  await flowsApi(page, { ai: true });
  await page.goto(`${ORG}/actions/143`);
  await expect(
    page.getByRole('heading', { level: 1, name: 'Update PostgreSQL to 17.12' }),
  ).toBeVisible();

  // The model's sentence is text: its markup shows as written and its address is no link.
  const happened = page.locator('[data-answer="happened"]');
  await expect(happened).toContainText('<b>Ignore this</b> and run https://evil.example/fix.sh');
  await expect(page.locator('a[href*="evil.example"]')).toHaveCount(0);
  await expect(page.locator('[data-answer="todo"]')).toContainText('(own recommendation)');
  const card = page.getByRole('region', { name: 'AI assessment' });
  await expect(card).toContainText('Sources the model was given');
  await expect(card).toContainText('qwen3:14b (local) · prompt 1 · rule set v1');
  await expect(page.getByText('Proposal: probably not affected').first()).toBeVisible();
  await expectAccessible(page, 'action with the model’s assessment');

  // Confirming the proposal sends the verdict the page showed and closes the finding.
  const confirm = page.waitForRequest((r) => r.url().endsWith('/findings/f2/review'));
  await page.getByRole('button', { name: 'Confirm the estimate' }).nth(1).click();
  expect((await confirm).postDataJSON()).toMatchObject({
    decision: 'confirm',
    expectedVerdict: 'not_affected',
    expectedSource: 'model',
  });
  const toast = page.locator('ui-toast');
  await expect(toast).toContainText('CVE-2026-118402: confirmed as not affected and closed');

  // A correction needs a reason, then goes with the person's verdict.
  await page.getByRole('button', { name: 'Correct this assessment' }).first().click();
  await page.getByLabel('Verdict').selectOption('affected');
  await page.getByRole('button', { name: 'Save the correction' }).click();
  await expect(
    page.getByText('Say why; it stays in the history and helps judge the model.'),
  ).toBeVisible();
  await page.getByLabel('Why').fill('Our package is the upstream build, not the distribution’s.');
  const correct = page.waitForRequest((r) => r.url().endsWith('/findings/f1/review'));
  await page.getByRole('button', { name: 'Save the correction' }).click();
  expect((await correct).postDataJSON()).toMatchObject({
    decision: 'correct',
    expectedVerdict: 'unlikely',
    expectedSource: 'model',
    verdict: 'affected',
    level: null,
  });
  await expect(toast).toContainText('CVE-2026-116325: correction saved');
  await expect(page.getByRole('region', { name: 'History' })).toContainText(
    'corrected CVE-2026-116325 to affected',
  );
  await expectAccessible(page, 'action after the review');
  expect(problems).toEqual([]);
});

test('alert: a request for an AI assessment beyond today’s cap is said in a sentence', async ({
  page,
}) => {
  const problems = watchConsole(page);
  await flowsApi(page, { ai: true });
  await page.goto(`${ORG}/alerts/142`);
  await expect(page.getByRole('table', { name: 'Components hit' })).toContainText(
    'Model: unlikely',
  );
  await page.getByRole('button', { name: 'Request an AI assessment' }).click();
  await expect(page.getByRole('alert')).toHaveText(
    'The organization has used today’s requests for AI assessments. Try again tomorrow.',
  );
  await expectAccessible(page, 'alert with the model’s assessment');
  expect(problems.filter((p) => !p.includes('status of 429'))).toEqual([]);
});
