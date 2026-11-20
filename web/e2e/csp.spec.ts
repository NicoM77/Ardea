import { expect, test } from '@playwright/test';
import { watchConsole } from './console';
import { api } from './mock-api';

/**
 * The production build under the production Content Security Policy (served by
 * scripts/serve-static.mjs): every page loads, translates and routes without a single console
 * error. Guards against code that needs eval or inline script, which the instance never allows.
 */
const PAGES: { path: string; signedIn: boolean; title: string; heading: string }[] = [
  { path: '/sign-in', signedIn: false, title: 'Sign in · Ardea', heading: 'Sign in' },
  {
    path: '/register',
    signedIn: false,
    title: 'Create your account · Ardea',
    heading: 'Create your account',
  },
  {
    path: '/confirm-email?userId=0190a000-0000-7000-8000-000000000001&token=abc',
    signedIn: false,
    title: 'Confirm your e-mail address · Ardea',
    heading: 'Confirm your e-mail address',
  },
  {
    path: '/invitations/sample-token',
    signedIn: false,
    title: 'Invitation · Ardea',
    heading: 'Join Example Org',
  },
  { path: '/o/example/overview', signedIn: true, title: 'Overview · Ardea', heading: 'Overview' },
  { path: '/o/example/notices', signedIn: true, title: 'Notices · Ardea', heading: 'Notices' },
  {
    path: '/o/example/reminders',
    signedIn: true,
    title: 'Reminders · Ardea',
    heading: 'Reminders',
  },
  { path: '/o/example/team', signedIn: true, title: 'Team · Ardea', heading: 'Team' },
  { path: '/o/example/projects', signedIn: true, title: 'Projects · Ardea', heading: 'Projects' },
  {
    path: '/o/example/projects/0190a000-0000-7000-8000-000000000010',
    signedIn: true,
    title: 'Project · Ardea',
    heading: 'Customer Portal',
  },
  {
    path: '/o/example/setup?step=technologies',
    signedIn: true,
    title: 'Setup · Ardea',
    heading: 'Set up your organization',
  },
  { path: '/o/example/settings', signedIn: true, title: 'Settings · Ardea', heading: 'Settings' },
  {
    path: '/organizations/new',
    signedIn: true,
    title: 'Create organization · Ardea',
    heading: 'Create organization',
  },
  { path: '/admin', signedIn: true, title: 'Administration · Ardea', heading: 'Administration' },
  {
    path: '/account/two-factor',
    signedIn: true,
    title: 'Two-factor authentication · Ardea',
    heading: 'Two-factor authentication',
  },
  {
    path: '/entries',
    signedIn: true,
    title: 'Vulnerabilities · Ardea',
    heading: 'Vulnerabilities',
  },
  {
    path: '/entries/CVE-2026-0001',
    signedIn: true,
    title: 'Entry · Ardea',
    heading: 'Sample: remote code execution in an example library',
  },
  { path: '/sources', signedIn: true, title: 'Sources · Ardea', heading: 'Sources' },
];

for (const target of PAGES) {
  test(`${target.path} loads under the production policy`, async ({ page }) => {
    const problems = watchConsole(page);
    await api(page, target.signedIn);

    await page.goto(target.path);
    await expect(page.getByRole('heading', { level: 1, name: target.heading })).toBeVisible();
    await expect(page).toHaveTitle(target.title);
    expect(problems).toEqual([]);
  });
}

test('an ICU plural renders under the production policy', async ({ page }) => {
  const problems = watchConsole(page);
  await api(page, true);
  await page.goto('/admin');
  await expect(page.getByText('1 job', { exact: true })).toBeVisible();
  expect(problems).toEqual([]);
});
