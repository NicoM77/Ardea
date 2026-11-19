import { expect, test } from '@playwright/test';
import { expectAccessible } from './axe';
import { watchConsole } from './console';
import { flowsApi } from './flows-api';
import { notifyApi } from './notify-api';
import { stackApi } from './stack-api';
import { PROJECT_ID } from './mock-api';

/**
 * The core screens of phase 3 (tasks 3.8 to 3.11) in the production build under the production
 * policy, against a stateful stand-in for the API (e2e/flows-api.ts): the overview on a busy and a
 * quiet day, the filters of the actions list, a status change with the 409 conflict and taking a
 * closing back, the three answers above the action bar at 390 × 844, and the alert. Phase 4 adds
 * the model's assessment: its texts as plain text, the proposal confirmed and a verdict corrected
 * (task 4.7), and a request for an assessment refused for today (task 4.3); the catalogue that learns
 * with its review list and the note on a hit through a learned entry (tasks 4.9, 4.10). Phase 5a adds
 * the stack (task 5a.4, e2e/stack-api.ts): the wizard with the stack only, and the stack page, also
 * at 390 × 844. Every page is checked
 * with axe (WCAG 2.2 AA) and must log no console error.
 */
const ORG = '/o/example';

test('overview on a busy day: the finding, the urgent actions with their answers and the warnings', async ({
  page,
}) => {
  const problems = watchConsole(page);
  await flowsApi(page);
  await page.goto(`${ORG}/overview`);

  await expect(page.getByRole('heading', { level: 1, name: 'Overview' })).toBeVisible();
  // A reminder has no deadline and no owner (master plan 17.5): the sentence speaks of exploitation and fixes.
  await expect(page.getByRole('status').filter({ hasText: 'critical action is open' })).toHaveText(
    /1 critical action is open\. 1 actively exploited, 1 with a fix\./,
  );
  await expect(page.getByRole('region', { name: 'Deadlines' })).toHaveCount(0);
  // The open actions, highest priority first, the first with its three answers.
  const urgent = page.getByRole('region', { name: 'Most urgent' });
  const actionLinks = urgent.getByRole('link', { name: /^(Update|Mitigate) / });
  await expect(actionLinks).toHaveCount(4);
  await expect(actionLinks.first()).toHaveText('Update Puppet Enterprise to 2025.12.1');
  await expect(urgent.getByRole('link', { name: 'All actions' })).toBeVisible();
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
    'No critical or high action is open.',
  );
  await expect(
    page
      .getByRole('region', { name: 'Most urgent' })
      .getByRole('link', { name: /^(Update|Mitigate) / }),
  ).toHaveCount(2);
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
    '4 open. 1 critical.',
  );
  // No owner and no time left: a reminder has neither (master plan 17.5).
  await expect(table.getByRole('columnheader')).toHaveText([
    'Priority',
    'Action',
    'Since',
    'Status',
  ]);
  await expect(page.getByRole('region', { name: 'Owners' })).toHaveCount(0);
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

  await page.getByRole('button', { name: 'Mark as remediated' }).click();
  await page.getByLabel('How was it remediated?').fill('Updated to 17.12.');
  await page.getByRole('button', { name: 'Close for all findings' }).click();
  await expect(page.getByRole('alert')).toHaveText(
    'Someone changed this action in the meantime. It shows the current state now; decide again.',
  );
  // Read again: someone else closed it as an accepted risk; the way back is to take the closing back.
  await expect(page.getByRole('button', { name: 'Reopen' })).toBeVisible();
  await expect(page.locator('#action-status')).toHaveValue('risk_accepted');
  expect(problems.filter((p) => !p.includes('status of 409'))).toEqual([]);
});

test('action: open or done, closed with a reason, the closing taken back', async ({ page }) => {
  const problems = watchConsole(page);
  await flowsApi(page);
  await page.goto(`${ORG}/actions/143`);
  await expect(
    page.getByRole('heading', { level: 1, name: 'Update PostgreSQL to 17.12' }),
  ).toBeVisible();
  await expectAccessible(page, 'action');

  // A reminder has no owner, no deadline and no «in review» (master plan 17.5).
  await expect(page.getByLabel('Owner')).toHaveCount(0);
  await expect(page.getByLabel('Due')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Take into review' })).toHaveCount(0);
  await expect(page.locator('#action-status option')).toHaveText([
    'Open',
    'Remediated',
    'Risk accepted',
    'Not affected',
  ]);

  await page.getByRole('button', { name: 'Mark as remediated' }).click();
  const toast = page.locator('ui-toast');
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
  await expect(page.getByRole('button', { name: 'Mark as remediated' })).toBeVisible();
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
    await expect(bar.getByRole('button', { name: 'Mark as remediated' })).toBeVisible();
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

/*
 * Phase 5 (tasks 5.5, 5.8): the notification pages against `notifyApi` (e2e/notify-api.ts). A rule is
 * created, a webhook added and tested, an external address confirmed through its link without signing in,
 * a notification read from the bell, and push turned on and off on this device in Chromium. The browser's
 * push service is replaced by a stand-in in the page: Chromium in a test has no connection to one.
 */
test('notification rules: the matrix of rules and channels, and a new rule', async ({ page }) => {
  const problems = watchConsole(page);
  const api = await notifyApi(page);
  await page.goto(`${ORG}/notification-rules`);

  const matrix = page.getByRole('table', { name: 'Who hears what' });
  await expect(matrix.getByRole('row')).toHaveCount(4);
  const critical = matrix.getByRole('row', { name: /Critical at once/ });
  await expect(critical.getByRole('cell').nth(2)).toContainText(/At once\s*Owners and admins/);
  await expectAccessible(page, 'notification rules');

  await page.getByRole('link', { name: 'New rule' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'New rule' })).toBeVisible();
  await page.getByLabel('Name', { exact: true }).fill('Webshop to the agency');
  await page.getByRole('button', { name: 'Medium' }).click();
  await page.getByRole('checkbox', { name: /Fix available/ }).check();
  await page.getByLabel('Channel', { exact: true }).selectOption('push');
  await page.getByRole('button', { name: 'Add recipient' }).click();
  await page.getByLabel('Recipient', { exact: true }).nth(1).selectOption('role:viewer');
  await page.getByLabel('Channel', { exact: true }).nth(1).selectOption('email');
  await page.getByRole('combobox', { name: 'When', exact: true }).nth(1).selectOption('digest');
  await expect(
    page.getByText('Medium actions and above in all projects reach Owners and admins by push'),
  ).toBeVisible();
  await expectAccessible(page, 'rule editor');

  await page.getByRole('button', { name: 'Save rule' }).click();
  await expect(page.locator('ui-toast')).toContainText('Rule Webshop to the agency saved');
  const saved = api.requests.find((r) => r.method === 'POST' && r.path.includes('/rules'))!;
  expect(saved.body).toMatchObject({
    name: 'Webshop to the agency',
    minLevel: 3,
    projectIds: [],
    targets: [
      { channel: 'push', recipientKind: 'role', recipientRef: 'admin', mode: 'immediate' },
      {
        channel: 'email',
        recipientKind: 'role',
        recipientRef: 'viewer',
        mode: 'digest',
        digestCron: '0 8 * * 1-5',
      },
    ],
  });
  expect((saved.body as { events: string[] }).events).toContain('action.fix_available');
  await expect(matrix.getByRole('row')).toHaveCount(5);
  expect(problems).toEqual([]);
});

test('recipients: a webhook is added, its secret shown once, and a test message goes out', async ({
  page,
}) => {
  const problems = watchConsole(page);
  await notifyApi(page);
  await page.goto(`${ORG}/notification-rules/recipients`);

  await page.getByLabel('Name').fill('SIEM');
  await page.locator('#hook-url').fill('https://siem.example.org/hooks/ardea');
  await page.getByRole('button', { name: 'Add webhook' }).click();
  await expect(page.getByText('whsec_sample-secret-shown-once')).toBeVisible();
  const hooks = page.getByRole('list', { name: 'Webhooks' });
  await expect(hooks).toContainText('siem.example.org');

  await hooks.getByRole('button', { name: 'Send test' }).click();
  await expect(page.getByText('SIEM answered 200. The test message arrived.')).toBeVisible();
  await expect(hooks).toContainText('200');
  await expectAccessible(page, 'recipients');
  expect(problems).toEqual([]);
});

test('an external address is confirmed through the link in its e-mail, without signing in', async ({
  page,
}) => {
  const problems = watchConsole(page);
  const api = await notifyApi(page, { signedIn: false });
  // The token is in the fragment: the page's own request carries none.
  await page.goto('/notifications/confirm#token=link-token');

  await expect(page.getByRole('heading', { level: 1, name: 'Confirm your address' })).toBeVisible();
  await expect(
    page.getByText('Example Org wants to send security alerts to support@agency.example.'),
  ).toBeVisible();
  // Opening the link changed nothing; the button does.
  expect(api.requests.filter((r) => r.path.includes('/confirm'))).toEqual([]);
  await expectAccessible(page, 'confirm link');

  await page.getByRole('button', { name: 'Confirm address' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Address confirmed' })).toBeVisible();
  const confirmed = api.requests.find((r) => r.path === '/api/v1/notify/addresses/confirm')!;
  expect(confirmed.body).toEqual({ token: 'link-token' });
  expect(api.requests.every((r) => !r.path.includes('link-token'))).toBe(true);
  expect(problems).toEqual([]);
});

test('the bell counts unread notifications; reading one in the inbox opens its action', async ({
  page,
}) => {
  const problems = watchConsole(page);
  await flowsApi(page);
  const api = await notifyApi(page, { base: false });
  await page.goto(`${ORG}/overview`);

  const bell = page.getByRole('link', { name: 'Notifications, 2 unread' });
  await expect(bell).toBeVisible();
  await bell.click();
  await expect(page.getByRole('heading', { level: 1, name: 'Notifications' })).toBeVisible();
  await expect(page.getByText('2 unread in Example Org')).toBeVisible();
  await expectAccessible(page, 'inbox');

  await page.getByRole('link', { name: /\[5 Critical\] #143 Puppet Enterprise/ }).click();
  await expect(page).toHaveURL(/\/o\/example\/actions\/143$/);
  await expect(page.getByRole('link', { name: 'Notifications, 1 unread' })).toBeVisible();
  const read = api.requests.find((r) => r.path.endsWith('/notifications/read'))!;
  expect((read.body as { ids: string[] }).ids).toHaveLength(1);
  expect(problems).toEqual([]);
});

test('push is turned on and off on this device in Chromium', async ({ page }) => {
  const problems = watchConsole(page);
  // The browser's push service is not reachable in a test: subscribing stays in the page.
  await page.addInitScript(() => {
    // Headless Chromium answers «denied» whatever the context grants.
    Object.defineProperty(Notification, 'permission', { get: () => 'granted' });
    Notification.requestPermission = async () => 'granted';
    let current: PushSubscription | null = null;
    const endpoint = 'https://fcm.googleapis.com/fcm/send/test-device';
    const keys = {
      p256dh:
        'BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4',
      auth: 'BTBZMqHH6r4Tts7J_aSIgg',
    };
    PushManager.prototype.getSubscription = async () => current;
    PushManager.prototype.subscribe = async (options?: PushSubscriptionOptionsInit) => {
      const key = options?.applicationServerKey as Uint8Array;
      current = {
        endpoint,
        expirationTime: null,
        options: { applicationServerKey: key.buffer, userVisibleOnly: true },
        toJSON: () => ({ endpoint, keys }),
        unsubscribe: async () => {
          current = null;
          return true;
        },
      } as unknown as PushSubscription;
      return current;
    };
  });
  const api = await notifyApi(page);
  await page.goto(`${ORG}/settings/notifications`);

  await expect(page.getByRole('heading', { level: 1, name: 'Your notifications' })).toBeVisible();
  await expectAccessible(page, 'notification settings');
  await page.getByRole('button', { name: 'Turn on push' }).click();
  await expect(page.getByText('On for this device')).toBeVisible();
  const subscribed = api.requests.find(
    (r) => r.method === 'POST' && r.path.includes('/me/push-subscriptions'),
  )!;
  expect(subscribed.body).toMatchObject({
    endpoint: 'https://fcm.googleapis.com/fcm/send/test-device',
    keys: { auth: 'BTBZMqHH6r4Tts7J_aSIgg' },
  });
  await expect(page.getByText('Chrome on Linux')).toBeVisible();

  await page.getByRole('button', { name: 'Turn off on this device' }).click();
  await expect(page.getByText('Off on this device')).toBeVisible();
  // This device turns push off by its endpoint.
  expect(api.requests.map((r) => `${r.method} ${r.path}`)).toContain(
    'DELETE /api/v1/me/push-subscriptions?endpoint=https://fcm.googleapis.com/fcm/send/test-device',
  );
  await expect(page.getByText('No device receives push yet.')).toBeVisible();

  // Muting a channel is the member's own setting.
  await page.getByRole('switch', { name: 'E-mail' }).click();
  await expect(page.getByRole('switch', { name: 'E-mail' })).toHaveAttribute(
    'aria-checked',
    'false',
  );

  // Signing out takes this browser's subscription away first, while the session still exists.
  await page.getByRole('button', { name: 'Turn on push' }).click();
  await expect(page.getByText('On for this device')).toBeVisible();
  await page.getByRole('button', { name: /^Account:/ }).click();
  await page.getByRole('menuitem', { name: 'Sign out' }).click();
  await expect
    .poll(() => api.requests.map((r) => `${r.method} ${r.path.split('?')[0]}`))
    .toEqual(
      expect.arrayContaining(['DELETE /api/v1/me/push-subscriptions', 'POST /api/v1/auth/logout']),
    );
  const order = api.requests.map((r) => r.path);
  expect(order.lastIndexOf('/api/v1/auth/logout')).toBeGreaterThan(
    order.findLastIndex((path) => path.startsWith('/api/v1/me/push-subscriptions?endpoint=')),
  );
  expect(problems).toEqual([]);
});

test('catalogue that learns: the note on a hit leads to the review list, where a proposal is confirmed', async ({
  page,
}) => {
  const problems = watchConsole(page);
  await flowsApi(page, { learned: true });
  await page.goto(`${ORG}/actions/143`);
  await expect(page.getByText('Via the learned catalogue entry postgresql')).toBeVisible();
  await page.getByRole('link', { name: 'Open the entry' }).click();
  await expect(page).toHaveURL(/\/sources\?learned=enterprisedb%2Fpostgresplus#catalogue$/);

  await page.goto('/sources');
  const card = page.getByRole('region', { name: 'Catalogue that learns' });
  await expect(card).toContainText('19.3%');
  await expect(card).toContainText('12 of 200');
  // The model's reason is text: its markup shows as written and its address is no link.
  await expect(card).toContainText(
    '<b>Ignore this</b> and adopt everything from https://evil.example/',
  );
  await expect(page.locator('a[href*="evil.example"]')).toHaveCount(0);
  await expect(card).toContainText('waits: not confident enough');
  await expectAccessible(page, 'sources with the catalogue that learns');

  const adopt = page.waitForRequest((r) =>
    r.url().endsWith('/learned/0190a000-0000-7000-8000-00000000c001/adopt'),
  );
  await card.getByRole('button', { name: 'Confirm' }).click();
  expect((await adopt).method()).toBe('POST');
  await expect(card.getByText('acme/widgetserver is mapped')).toBeVisible();
  expect(problems).toEqual([]);
});

test('wizard with the stack only: products from the catalogue and the suggestions, a version line, a threshold, projects skipped', async ({
  page,
}) => {
  const problems = watchConsole(page);
  await flowsApi(page, { calm: true });
  const stack = await stackApi(page, { wizard: true });
  await page.goto(`${ORG}/setup`);

  const step = page.getByRole('region', { name: 'Your stack' });
  await expect(step).toBeVisible();
  await expect(step).toContainText('could this affect you?');
  await expect(page.getByRole('navigation', { name: 'Setup steps' })).toContainText('Stack');

  // Continue needs one product: the step says so and puts the focus in the search.
  await page.getByRole('button', { name: 'Continue' }).click();
  await expect(step.getByText('Add at least one product your organization runs.')).toBeVisible();
  const search = page.getByRole('combobox', { name: 'Add a product' });
  await expect(search).toBeFocused();

  // Another spelling finds the product; the keyboard picks it.
  await search.fill('postgres');
  await expect(page.getByRole('option', { name: /PostgreSQL/ })).toBeVisible();
  await search.press('ArrowDown');
  await search.press('Enter');
  const rows = step.getByRole('list', { name: 'Stack entries' }).getByRole('listitem');
  await expect(rows).toHaveCount(1);
  await expect(page.getByRole('status').filter({ hasText: 'Added PostgreSQL.' })).toBeVisible();
  await expect(search).toHaveValue('');
  await expect(search).toBeFocused();

  // Azure is not one product of the catalogue (ADR 044): the search says so.
  await search.fill('azure');
  await expect(page.getByText('Microsoft Azure is not one product in the catalogue')).toBeVisible();

  // IIS brings Windows Server as its companion (ADR 044), with the reason.
  await search.fill('iis');
  await page.getByRole('option', { name: /Internet Information Services/ }).click();
  const suggested = page.getByRole('region', { name: 'Suggested' });
  await expect(suggested).toContainText(
    'Vulnerabilities in Internet Information Services are usually published under Windows Server.',
  );
  await suggested.getByRole('button', { name: 'Add Windows Server to the stack' }).click();
  // A usual entry from the chips of its group.
  await suggested.getByRole('button', { name: 'Add nginx to the stack' }).click();
  await expect(rows).toHaveCount(4);

  // A version line for PostgreSQL, and every entry for nginx; each is saved at once.
  const postgres = rows.filter({ hasText: 'PostgreSQL' });
  const version = postgres.getByRole('textbox', { name: 'Version of PostgreSQL' });
  await version.fill('17');
  await expect(postgres).toContainText('Only entries for this line');
  await version.press('Enter');
  await expect(postgres.getByRole('combobox', { name: /How exact/ })).toHaveValue('line');
  const nginx = rows.filter({ hasText: 'nginx' });
  await nginx.getByRole('button', { name: 'Everything' }).click();
  await expect(nginx.getByRole('button', { name: 'Everything' })).toHaveAttribute(
    'aria-pressed',
    'true',
  );
  expect(stack.writes.filter((w) => w.method === 'PATCH').map((w) => w.body)).toEqual([
    expect.objectContaining({ version: '17', versionPrecision: 'line' }),
    expect.objectContaining({ noticeThreshold: 'everything' }),
  ]);
  await expectAccessible(page, 'wizard stack step');

  // Continue: the focus moves to the next step's title; the project is optional.
  await page.getByRole('button', { name: 'Continue' }).click();
  await expect(page).toHaveURL(/\/setup\?step=project$/);
  await expect(page.getByRole('heading', { name: /Step 3: Project/ })).toBeFocused();
  await expect(page.getByText('Optional: your stack is enough to start.')).toBeVisible();
  await expectAccessible(page, 'wizard project step, optional');
  await page.getByRole('button', { name: 'Skip — the stack is enough to start' }).click();
  await expect(page).toHaveURL(/\/o\/example\/overview$/);
  await expect(page.getByRole('status').filter({ hasText: 'Setup finished.' })).toBeVisible();
  expect(problems).toEqual([]);
});

test('stack page: suggestions with their reasons, a removal taken back, a version cleared', async ({
  page,
}) => {
  const problems = watchConsole(page);
  await flowsApi(page);
  const stack = await stackApi(page);
  await page.goto(`${ORG}/overview`);
  await page.getByRole('link', { name: 'Stack' }).click();
  await expect(page).toHaveURL(/\/o\/example\/stack$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Stack' })).toBeVisible();
  await expect(page.getByText('2 entries, 1 with a version line.')).toBeVisible();

  const entries = page.getByRole('region', { name: 'Products the organization runs' });
  const rows = entries.getByRole('list', { name: 'Stack entries' }).getByRole('listitem');
  await expect(rows).toHaveCount(2);
  const suggested = page.getByRole('region', { name: 'Suggested' });
  await expect(suggested).toContainText(
    'Ubuntu covers the distribution, not the Linux kernel it ships.',
  );
  await expect(suggested).toContainText('Named in Platform Ops.');
  await expect(page.getByRole('region', { name: 'What raises a notice' })).toContainText(
    'Critical (CVSS 9.0 or more)',
  );
  await expectAccessible(page, 'stack page');

  // Removing Ubuntu moves the focus to the next Remove; Undo brings it back as it was.
  await rows.filter({ hasText: 'Ubuntu' }).getByRole('button', { name: 'Remove Ubuntu' }).click();
  await expect(rows).toHaveCount(1);
  await expect(page.getByRole('button', { name: 'Remove PostgreSQL' })).toBeFocused();
  await expect(suggested).not.toContainText('Linux kernel');
  await page.getByRole('button', { name: 'Undo' }).click();
  await expect(rows).toHaveCount(2);
  await expect(rows.filter({ hasText: 'Ubuntu' })).toBeVisible();

  // Clearing the version makes it any version again.
  const postgres = rows.filter({ hasText: 'PostgreSQL' });
  await postgres.getByRole('textbox', { name: 'Version of PostgreSQL' }).fill('');
  await postgres.getByRole('textbox', { name: 'Version of PostgreSQL' }).press('Enter');
  await expect(postgres).toContainText('Any version');
  await expect(postgres.getByRole('combobox')).toHaveCount(0);
  expect(stack.writes.map((w) => w.method)).toEqual(['DELETE', 'POST', 'PATCH']);
  expect(problems).toEqual([]);
});

test('stack page: a save that fails says so, and the row shows what is stored', async ({
  page,
}) => {
  const problems = watchConsole(page);
  await flowsApi(page);
  const stack = await stackApi(page);
  await page.goto(`${ORG}/stack`);
  const rows = page.getByRole('list', { name: 'Stack entries' }).getByRole('listitem');
  await expect(rows).toHaveCount(2);

  stack.failWrites = true;
  const ubuntu = rows.filter({ hasText: 'Ubuntu' });
  const version = ubuntu.getByRole('textbox', { name: 'Version of Ubuntu' });
  await version.fill('24.04');
  await version.press('Enter');
  await expect(page.getByText('The change could not be saved. Try again.')).toBeVisible();
  await expect(version).toHaveValue('');
  await expect(ubuntu).toContainText('Any version');

  const postgres = rows.filter({ hasText: 'PostgreSQL' });
  const exactness = postgres.getByRole('combobox', { name: /How exact/ });
  await exactness.focus();
  await exactness.selectOption('exact');
  await expect(exactness).toHaveValue('line');
  await expect(exactness).toBeFocused();

  // Once writes go through again, the same change is saved.
  stack.failWrites = false;
  await version.fill('24.04');
  await version.press('Enter');
  await expect(page.getByText('The change could not be saved. Try again.')).toHaveCount(0);
  await expect(ubuntu.getByRole('combobox', { name: /How exact/ })).toHaveValue('exact');
  expect(problems.filter((p) => !p.includes('status of 409'))).toEqual([]);
});

test('wizard: Undo still takes an addition back after Continue left the stack step', async ({
  page,
}) => {
  const problems = watchConsole(page);
  await flowsApi(page, { calm: true });
  const stack = await stackApi(page, { wizard: true });
  await page.goto(`${ORG}/setup`);
  const suggested = page.getByRole('region', { name: 'Suggested' });
  await suggested.getByRole('button', { name: 'Add PostgreSQL to the stack' }).click();
  await suggested.getByRole('button', { name: 'Add nginx to the stack' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Added nginx.' })).toBeVisible();

  await page.getByRole('button', { name: 'Continue' }).click();
  await expect(page).toHaveURL(/\/setup\?step=project$/);
  await page.getByRole('button', { name: 'Undo' }).click();
  await expect.poll(() => stack.writes.map((w) => w.method)).toEqual(['POST', 'POST', 'DELETE']);

  await page.getByRole('button', { name: 'Back' }).click();
  const rows = page.getByRole('list', { name: 'Stack entries' }).getByRole('listitem');
  await expect(rows).toHaveCount(1);
  await expect(rows).toContainText('PostgreSQL');
  expect(problems).toEqual([]);
});

test.describe('the stack at 390 × 844', () => {
  test.use({ viewport: { width: 390, height: 844 }, hasTouch: true });

  test('fits the width and stays usable', async ({ page }) => {
    const problems = watchConsole(page);
    await flowsApi(page);
    await stackApi(page);
    await page.goto(`${ORG}/stack`);
    const rows = page.getByRole('list', { name: 'Stack entries' }).getByRole('listitem');
    await expect(rows).toHaveCount(2);
    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );
    expect(overflow).toBeLessThanOrEqual(0);
    await rows.filter({ hasText: 'Ubuntu' }).getByRole('button', { name: 'Everything' }).click();
    await expect(
      rows.filter({ hasText: 'Ubuntu' }).getByRole('button', { name: 'Everything' }),
    ).toHaveAttribute('aria-pressed', 'true');
    await expectAccessible(page, 'stack page on a phone');
    expect(problems).toEqual([]);
  });
});
