import type { Page } from '@playwright/test';

/** API answers for the Playwright tests: a signed-in instance administrator, or a signed-out visitor. */

const SESSION = {
  user: {
    id: '0190a000-0000-7000-8000-000000000001',
    email: 'sample.person@example.org',
    displayName: 'Sample Person',
    isInstanceAdmin: true,
    twoFactorEnabled: false,
  },
  organizations: [
    {
      id: '0190a000-0000-7000-8000-000000000002',
      slug: 'example',
      name: 'Example Org',
      role: 'owner',
    },
  ],
};
const SETTING = (value: string | null, locked = false) => ({
  value,
  isSet: value !== null,
  locked,
});

export async function api(page: Page, signedIn: boolean): Promise<void> {
  await page.route('**/api/v1/**', (route) => {
    const path = new URL(route.request().url()).pathname;
    switch (path) {
      case '/api/v1/setup/status':
        return route.fulfill({ json: { setupRequired: false } });
      case '/api/v1/auth/session':
        return signedIn
          ? route.fulfill({ json: SESSION })
          : route.fulfill({ status: 401, json: { status: 401, code: 'unauthorized' } });
      case '/api/v1/instance':
        return route.fulfill({
          json: { version: '0.1.0', jobs: { pending: 2, running: 1, dead: 1, failed: '0' } },
        });
      case '/api/v1/instance/settings':
        return route.fulfill({
          json: {
            registration: SETTING('invite'),
            smtp: {
              host: SETTING('mail.example.org'),
              port: SETTING('587'),
              user: SETTING(null),
              password: { value: null, isSet: false, locked: false },
              from: SETTING('ardea@example.org', true),
              security: SETTING('starttls'),
            },
          },
        });
      case '/api/v1/instance/jobs':
        return route.fulfill({
          json: [
            {
              id: '0190a000-0000-7000-8000-000000000003',
              kind: 'iam.send-email-confirmation',
              state: 'dead',
              attempts: 5,
              maxAttempts: 5,
              lastError: 'Connection refused (mail.example.org:587)',
              createdAt: '2026-11-18T08:00:00Z',
              finishedAt: '2026-11-18T08:20:00Z',
            },
          ],
        });
      default:
        return route.fulfill({ status: 204 });
    }
  });
}
