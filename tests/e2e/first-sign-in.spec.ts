import { expect, test } from '@playwright/test';
import { Authenticator, expectAccessible, openFirstPage, watchConsole } from './support';

const setupToken = process.env['E2E_SETUP_TOKEN'] ?? '';
const person = {
  email: 'first.admin@example.org',
  name: 'First Admin',
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

  await openFirstPage(page, '/');
  await expect(page).toHaveURL(/\/setup$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Set up Ardea' })).toBeVisible();
  await expectAccessible(page, 'setup');

  await page.getByLabel('Setup token').fill(setupToken);
  await page.getByLabel('Email').fill(person.email);
  await page.getByLabel('Name').fill(person.name);
  await page.getByLabel('Password', { exact: true }).fill(person.password);
  await page.getByRole('button', { name: 'Create account' }).click();

  await expect(page.getByRole('heading', { level: 1, name: 'Welcome' })).toBeVisible();
  const navigation = page.getByRole('navigation', { name: 'Main' });
  await expect(navigation.getByRole('link', { name: 'Administration' })).toBeVisible();
  await expectAccessible(page, 'no organization');

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

  await page.getByRole('button', { name: `Account: ${person.name}` }).click();
  await page.getByRole('menuitem', { name: 'Sign out' }).click();
  await expect(page).toHaveURL(/\/sign-in\?notice=signedOut$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Sign in' })).toBeVisible();
  await expectAccessible(page, 'sign in');

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

  await expect(page.getByRole('heading', { level: 1, name: 'Welcome' })).toBeVisible();
  await expect(navigation.getByRole('link', { name: 'Administration' })).toBeVisible();
  await navigation.getByRole('link', { name: 'Administration' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Administration' })).toBeVisible();
  await expect(page.getByText('Installed version')).toBeVisible();
  await expectAccessible(page, 'administration');

  await navigation.getByRole('link', { name: 'Sources' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Sources' })).toBeVisible();
  await expect(page.getByRole('heading', { level: 2 }).first()).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Data-quality review' })).toBeVisible();
  await expectAccessible(page, 'sources');

  await navigation.getByRole('link', { name: 'Vulnerabilities' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Vulnerabilities' })).toBeVisible();
  await expect(page.locator('section[aria-busy="false"]')).toBeVisible();
  await expectAccessible(page, 'entries');

  await page.getByRole('link', { name: /No organization/ }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Create organization' })).toBeVisible();
  await page.getByLabel('Name').fill('Heron Works');
  await expect(page.getByText('/o/heron-works')).toBeVisible();
  await expectAccessible(page, 'create organization');
  await page.getByRole('button', { name: 'Create organization' }).click();

  await expect(page).toHaveURL(/\/o\/heron-works\/setup/);
  await expect(
    page.getByRole('heading', { level: 1, name: 'Set up your organization' }),
  ).toBeVisible();
  const stack = page.getByRole('region', { name: 'Your stack' });
  await expect(stack).toBeVisible();
  const product = page.getByRole('combobox', { name: 'Add a product' });
  await product.fill('postgres');
  await expect(page.getByRole('option', { name: /PostgreSQL/ }).first()).toBeVisible();
  await product.press('ArrowDown');
  await product.press('Enter');
  await expect(
    stack.getByRole('list', { name: 'Stack entries' }).getByRole('listitem'),
  ).toHaveCount(1);
  await expectAccessible(page, 'wizard stack');
  await page.getByRole('button', { name: 'Continue' }).click();
  await expect(page).toHaveURL(/\/setup\?step=project$/);
  await page.getByLabel('Name').fill('Customer Portal');
  await page.getByRole('button', { name: 'Continue' }).click();
  await expect(page.getByRole('heading', { name: 'Where Ardea reads from' })).toBeVisible();
  await page.getByRole('button', { name: 'Skip: no repository' }).click();
  await expect(page.getByRole('heading', { name: 'How it is reached' })).toBeVisible();
  await page.getByRole('radio', { name: /^Internet-facing/ }).check();
  await page.getByRole('radio', { name: /^High criticality/ }).check();
  await expectAccessible(page, 'wizard operations');
  await page.getByRole('button', { name: 'Continue' }).click();

  await page.getByRole('searchbox', { name: 'Search the catalogue' }).fill('PostgreSQL');
  await page.getByRole('button', { name: 'Add PostgreSQL' }).click();
  await page
    .getByRole('group', { name: 'PostgreSQL' })
    .getByLabel('Version', { exact: true })
    .fill('18.1');
  await expectAccessible(page, 'wizard technologies');
  await page.getByRole('button', { name: 'Continue' }).click();
  await expect(page.getByRole('heading', { name: 'Coverage', exact: true })).toBeVisible();
  await expectAccessible(page, 'wizard coverage');
  await page.getByRole('button', { name: 'Finish setup' }).click();

  await expect(page).toHaveURL(/\/o\/heron-works\/projects\/[0-9a-f-]+$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Customer Portal' })).toBeVisible();
  await expect(page.getByRole('table', { name: 'Platforms and services' })).toContainText(
    'PostgreSQL',
  );
  await expect(page.getByRole('table', { name: 'Platforms and services' })).toContainText('18.1');
  await expect(page.getByText('Internet-facing')).toBeVisible();
  await expectAccessible(page, 'project');

  await navigation.getByRole('link', { name: 'Team' }).click();
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
  await expect(page.getByRole('button', { name: 'Leave organization' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Send invitation' })).toHaveCount(0);

  expect(problems).toEqual([]);
});
