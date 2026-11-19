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
      case '/api/v1/entries':
        return route.fulfill({ json: { items: [ENTRY], nextCursor: null } });
      case `/api/v1/entries/${ENTRY.key}`:
        return route.fulfill({ json: ENTRY_DETAIL });
      case `/api/v1/entries/${ENTRY.key}/changes`:
        return route.fulfill({
          json: [
            {
              revision: 2,
              changedAt: '2026-11-18T08:00:00Z',
              kinds: ['signals'],
              isMaterial: true,
              triggeredBy: 'cisa-kev',
              diff: null,
            },
          ],
        });
      case `/api/v1/entries/${ENTRY.key}/sources`:
        return route.fulfill({
          json: [
            {
              source: 'cve-program',
              name: 'CVE Program',
              operator: 'CVE Program',
              operatorKind: 'mandated',
              relation: 'primary',
              url: 'https://www.cve.org/CVERecord?id=CVE-2026-0001',
              attribution: 'Sample attribution',
            },
          ],
        });
      case '/api/v1/sources':
        return route.fulfill({ json: SOURCES });
      case '/api/v1/instance/review':
        return route.fulfill({
          json: [
            {
              id: 'case-1',
              kind: 'cve_conflict',
              state: 'open',
              entries: [
                { primaryKey: 'CVE-2026-0001', title: 'Sample A', state: 'published' },
                { primaryKey: 'CVE-2026-0002', title: 'Sample B', state: 'published' },
              ],
              sharedAliases: ['GHSA-aaaa-bbbb-cccc'],
              firstSeenAt: '2026-11-17T08:00:00Z',
              lastSeenAt: '2026-11-18T08:00:00Z',
              decidedAt: null,
            },
          ],
        });
      default:
        return route.fulfill({ status: 204 });
    }
  });
}

const ENTRY = {
  key: 'CVE-2026-0001',
  kind: 'vulnerability',
  title: 'Sample: remote code execution in an example library',
  state: 'published',
  cvssScore: 9.8,
  severity: 'critical',
  exploitation: 'active',
  epssClass: 'high',
  fixAvailable: true,
  authorityWarning: false,
  publishedAt: '2026-11-17T08:00:00Z',
  changedAt: '2026-11-18T08:00:00Z',
  aliases: ['CVE-2026-0001', 'GHSA-aaaa-bbbb-cccc'],
};

const ENTRY_DETAIL = {
  entry: ENTRY,
  summary: null,
  description: 'Sample description for the end-to-end test.',
  cvssVersion: '3.1',
  cvssVector: 'CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H',
  cweIds: ['CWE-94'],
  provenance: { title: 'cve-program', cvss: 'cve-program' },
  exploitationEvidence: [
    { source: 'cisa-kev', state: 'active', evidence: null, since: '2026-11-18T00:00:00Z' },
  ],
  metrics: [],
  solutions: ['Update to 2.4.2.'],
  workarounds: [],
  affected: [
    {
      vendor: 'Example',
      product: 'library',
      productKey: null,
      cpe: null,
      purl: null,
      versionScheme: 'semver',
      introduced: '2.0.0',
      fixed: '2.4.2',
      lastAffected: null,
      exact: null,
      status: 'affected',
      defaultStatus: 'unaffected',
      source: 'cve-program',
    },
  ],
  references: [{ url: 'https://www.cve.org/CVERecord?id=CVE-2026-0001', tags: ['advisory'] }],
  revision: 2,
  redirectedFrom: null,
};

const SOURCES = [
  {
    key: 'cve-program',
    name: 'CVE Program',
    kind: 'feed',
    operator: 'CVE Program',
    operatorKind: 'mandated',
    enabled: true,
    health: 'healthy',
    healthReason: null,
    intervalMinutes: 60,
    lastSuccessAt: '2026-11-18T08:00:00Z',
    lastAttemptAt: '2026-11-18T08:00:00Z',
    nextSyncAt: '2026-11-18T09:00:00Z',
    consecutiveFailures: 0,
    records: 6432,
    quarantined: 2,
    attribution: 'Sample attribution',
    termsMode: 'full',
  },
  {
    key: 'bacs-warnings',
    name: 'BACS warnings',
    kind: 'website',
    operator: 'BACS',
    operatorKind: 'authority',
    enabled: true,
    health: 'degraded',
    healthReason: 'The last fetch returned no records.',
    intervalMinutes: 30,
    lastSuccessAt: '2026-11-18T07:30:00Z',
    lastAttemptAt: '2026-11-18T08:00:00Z',
    nextSyncAt: '2026-11-18T08:30:00Z',
    consecutiveFailures: 1,
    records: 12,
    quarantined: 0,
    attribution: 'Sample attribution',
    termsMode: 'excerpt',
  },
];
