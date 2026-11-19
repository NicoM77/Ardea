import { expect, test } from '@playwright/test';
import { Authenticator, expectAccessible, openFirstPage, watchConsole } from './support';

/**
 * Core flow 1 of master plan 13.6 and the acceptance of phase 0: on a fresh instance, set up the
 * first user with the setup token, turn on two-factor authentication, sign out, and sign in again
 * with password and TOTP over HTTPS. Then the core flow «accept an invitation»: create an
 * organization, invite a second person, who registers through the link and joins. Every visited
 * page is checked with axe (WCAG 2.2 AA) and the test fails on any console error.
 */
const setupToken = process.env['E2E_SETUP_TOKEN'] ?? '';
const person = {
  email: 'first.admin@example.org',
  name: 'First Admin',
  // A long passphrase: the password rules ask for length, not character classes.
  password: 'heron stands still in the reeds',
};
const invitee = {
  email: 'invited.viewer@example.org',
  name: 'Invited Viewer',
  password: 'egret wades through the shallows',
};

test('set up the first user, turn on two-factor, sign in again with TOTP', async ({ page }) => {
  test.skip(!setupToken, 'E2E_SETUP_TOKEN is not set; run the test through run.sh');
  const problems = watchConsole(page);

  // A fresh instance sends every visitor to setup.
  await openFirstPage(page, '/');
  await expect(page).toHaveURL(/\/setup$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Set up Ardea' })).toBeVisible();
  await expectAccessible(page, 'setup');

  await page.getByLabel('Setup token').fill(setupToken);
  await page.getByLabel('Email').fill(person.email);
  await page.getByLabel('Name').fill(person.name);
  await page.getByLabel('Password', { exact: true }).fill(person.password);
  await page.getByRole('button', { name: 'Create account' }).click();

  // Signed in, without an organization yet; the instance administrator sees Administration.
  await expect(page.getByRole('heading', { level: 1, name: 'Welcome' })).toBeVisible();
  const navigation = page.getByRole('navigation', { name: 'Main' });
  await expect(navigation.getByRole('link', { name: 'Administration' })).toBeVisible();
  await expectAccessible(page, 'no organization');

  // Two-factor authentication: confirm the password, read the key, enter a code, see the recovery codes.
  await page.getByRole('button', { name: `Account: ${person.name}` }).click();
  await page.getByRole('menuitem', { name: 'Two-factor authentication' }).click();
  await expect(
    page.getByRole('heading', { level: 1, name: 'Two-factor authentication' }),
  ).toBeVisible();
  await expectAccessible(page, 'two-factor intro');

  await page.getByLabel('Current password').fill(person.password);
  await page.getByRole('button', { name: 'Set up authenticator app' }).click();
  const appLink = page.getByRole('link', { name: 'Open in authenticator app' });
  await expect(appLink).toBeVisible();
  await expectAccessible(page, 'two-factor scan');

  const uri = new URL((await appLink.getAttribute('href')) ?? '');
  const sharedKey = uri.searchParams.get('secret') ?? '';
  expect(sharedKey).not.toBe('');
  // The key shown for typing by hand is the same one, in groups of four.
  await expect(page.getByText(sharedKey.match(/.{1,4}/g)!.join(' '))).toBeVisible();
  const authenticator = new Authenticator(sharedKey);

  await page.getByLabel('6-digit code').fill(await authenticator.nextCode());
  await page.getByRole('button', { name: 'Turn on' }).click();
  await expect(page.getByRole('heading', { name: 'Save your recovery codes' })).toBeVisible();
  await expect(
    page.getByRole('list', { name: 'Recovery codes' }).getByRole('listitem'),
  ).not.toHaveCount(0);
  await expectAccessible(page, 'recovery codes');
  await page.getByRole('button', { name: 'I saved them' }).click();

  // Sign out.
  await page.getByRole('button', { name: `Account: ${person.name}` }).click();
  await page.getByRole('menuitem', { name: 'Sign out' }).click();
  await expect(page).toHaveURL(/\/sign-in\?notice=signedOut$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Sign in' })).toBeVisible();
  await expectAccessible(page, 'sign in');

  // Sign in with the password, then the second step with a fresh code.
  await page.getByLabel('Email').fill(person.email);
  await page.getByLabel('Password', { exact: true }).fill(person.password);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page).toHaveURL(/\/sign-in\/two-factor/);
  await expect(
    page.getByRole('heading', { level: 1, name: 'Two-factor authentication' }),
  ).toBeVisible();
  await expectAccessible(page, 'two-factor step');

  await page.getByLabel('Code').fill(await authenticator.nextCode());
  await page.getByRole('button', { name: 'Verify' }).click();

  // Signed in: the shell with the instance administrator's Administration.
  await expect(page.getByRole('heading', { level: 1, name: 'Welcome' })).toBeVisible();
  await expect(navigation.getByRole('link', { name: 'Administration' })).toBeVisible();
  await navigation.getByRole('link', { name: 'Administration' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Administration' })).toBeVisible();
  await expect(page.getByText('Installed version')).toBeVisible();
  await expectAccessible(page, 'administration');

  // Entries and sources (task 1.10): instance-wide, reachable without an organization.
  await navigation.getByRole('link', { name: 'Sources' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Sources' })).toBeVisible();
  await expect(page.getByRole('heading', { level: 2 }).first()).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Data-quality review' })).toBeVisible();
  await expectAccessible(page, 'sources');

  await navigation.getByRole('link', { name: 'Vulnerabilities' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Vulnerabilities' })).toBeVisible();
  await expect(page.locator('section[aria-busy="false"]')).toBeVisible();
  await expectAccessible(page, 'entries');

  // Task 2.1 and core flow «accept an invitation» (master plan 13.6): create an organization,
  // invite someone, and let them join through the link. The instance sends no e-mail, so the
  // Team page hands the link over once.
  // Without an organization yet, the switch in the sidebar leads to creating one.
  await page.getByRole('link', { name: /No organization/ }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Create organization' })).toBeVisible();
  await page.getByLabel('Name').fill('Heron Works');
  await expect(page.getByText('/o/heron-works')).toBeVisible();
  await expectAccessible(page, 'create organization');
  await page.getByRole('button', { name: 'Create organization' }).click();

  await expect(page).toHaveURL(/\/o\/heron-works\/team$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Team' })).toBeVisible();
  await expect(page.getByRole('list', { name: 'Members' }).getByText(person.email)).toBeVisible();
  await page.getByLabel('Email').fill(invitee.email);
  await page.getByLabel('Role', { exact: true }).selectOption('viewer');
  await page.getByRole('button', { name: 'Send invitation' }).click();
  const link = await page.getByLabel('Invitation link').inputValue();
  expect(link).toMatch(/\/invitations\/[\w-]+$/);
  await expect(page.getByRole('list', { name: 'Open invitations' })).toContainText(invitee.email);
  await expectAccessible(page, 'team');

  await page.getByRole('button', { name: `Account: ${person.name}` }).click();
  await page.getByRole('menuitem', { name: 'Sign out' }).click();
  await expect(page).toHaveURL(/\/sign-in\?notice=signedOut$/);

  // The invited person opens the link, creates an account with the address filled in, and joins.
  await page.goto(link);
  await expect(page.getByRole('heading', { level: 1, name: 'Join Heron Works' })).toBeVisible();
  await expectAccessible(page, 'invitation');
  await page.getByRole('link', { name: 'Create account' }).click();
  await expect(page.getByLabel('Email')).toHaveValue(invitee.email);
  await page.getByLabel('Name').fill(invitee.name);
  await page.getByLabel('Password', { exact: true }).fill(invitee.password);
  await page.getByRole('button', { name: 'Create account' }).click();

  await expect(page).toHaveURL(/\/o\/heron-works\/overview$/);
  await navigation.getByRole('link', { name: 'Team' }).click();
  const members = page.getByRole('list', { name: 'Members' });
  await expect(members.getByRole('listitem')).toHaveCount(2);
  await expect(members.getByText(invitee.email)).toBeVisible();
  // A viewer reads the directory and may leave, but invites no one.
  await expect(page.getByRole('button', { name: 'Leave organization' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Send invitation' })).toHaveCount(0);

  expect(problems).toEqual([]);
});
