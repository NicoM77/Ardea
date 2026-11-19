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
      mfaRequired: false,
    },
  ],
};
export const ORG_ID = SESSION.organizations[0].id;
export const USER_ID = SESSION.user.id;
const INVITATION = {
  id: '0190a000-0000-7000-8000-000000000005',
  email: 'invited.person@example.org',
  role: 'member',
  expiresAt: '2026-11-25T08:00:00Z',
  expired: false,
  invitedBy: 'Sample Person',
  createdAt: '2026-11-18T08:00:00Z',
};
export const PROJECT_ID = '0190a000-0000-7000-8000-000000000010';
const PROJECT = {
  id: PROJECT_ID,
  name: 'Customer Portal',
  description: 'Online self-service for customers.',
  criticality: 'high',
  exposure: 'internet',
  archived: false,
  componentCount: 3,
  createdAt: '2026-11-18T08:00:00Z',
  changedAt: '2026-11-18T08:00:00Z',
};
const COMPONENT = {
  id: '0190a000-0000-7000-8000-000000000011',
  productKey: 'aspnetcore',
  name: 'ASP.NET Core',
  vendor: 'Microsoft',
  inCatalog: true,
  kind: 'framework',
  version: '10.0',
  versionScheme: 'semver',
  packaging: 'unknown',
  parentId: null,
  exposure: null,
  criticality: null,
  protection: 'none',
  protectionNote: null,
  origin: 'repository',
  purl: null,
  dependencyScope: null,
  dependencyRelation: null,
};
const COMPONENTS = [
  COMPONENT,
  {
    ...COMPONENT,
    id: '0190a000-0000-7000-8000-000000000012',
    productKey: 'azure-sql-database',
    name: 'Azure SQL Database',
    kind: 'service',
    version: null,
    origin: 'manual',
  },
  {
    ...COMPONENT,
    id: '0190a000-0000-7000-8000-000000000013',
    productKey: null,
    name: 'rxjs',
    inCatalog: false,
    kind: 'library',
    version: '7.8.2',
    purl: 'pkg:npm/rxjs@7.8.2',
    dependencyScope: 'runtime',
    dependencyRelation: 'direct',
  },
];
const CONNECTION = {
  id: '0190a000-0000-7000-8000-000000000014',
  name: 'Example on GitHub',
  hostId: null,
  hostName: 'github.com',
  tokenKind: 'fine_grained',
  tokenHint: 'a91F',
  tokenExpiresAt: '2027-02-12T00:00:00Z',
  expiresSoon: false,
  warnings: [],
  login: 'ardea-reader',
  lastOkAt: '2026-11-18T08:00:00Z',
  lastError: null,
  createdAt: '2026-11-17T08:00:00Z',
};
const LINK = {
  id: '0190a000-0000-7000-8000-000000000015',
  connectionId: CONNECTION.id,
  connectionName: CONNECTION.name,
  owner: 'example',
  name: 'portal',
  ref: null,
  path: null,
  state: 'partial',
  stateReason: 'The dependency graph is not enabled for this repository.',
  failingSince: null,
  lastCommitSha: '4f2a9c1d0e',
  lastIndexedAt: '2026-11-18T08:00:00Z',
  nextCheckAt: '2026-11-18T09:00:00Z',
  shrinkPending: false,
  componentCount: 2,
};
const SUGGESTION = {
  id: '0190a000-0000-7000-8000-000000000016',
  linkId: LINK.id,
  productKey: 'nginx',
  productName: 'nginx',
  version: '1.29',
  evidence: [{ file: 'web/Dockerfile', line: 9, value: 'FROM nginx:1.29-alpine' }],
  state: 'open',
  componentId: null,
};
const SETTING = (value: string | null, locked = false) => ({
  value,
  isSet: value !== null,
  locked,
});

export async function api(page: Page, signedIn: boolean): Promise<void> {
  await page.route('**/api/v1/**', (route) => {
    const path = new URL(route.request().url()).pathname;
    // The organization's live stream: one `ready`, then it ends; the long retry keeps the browser
    // from opening it again during a test.
    if (/^\/api\/v1\/orgs\/[^/]+\/events$/.test(path))
      return route.fulfill({
        status: 200,
        contentType: 'text/event-stream',
        body: 'retry: 600000\nevent: ready\ndata: {}\n\n',
      });
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
      case `/api/v1/orgs/${ORG_ID}/members`:
        return route.fulfill({
          json: [
            {
              userId: SESSION.user.id,
              displayName: SESSION.user.displayName,
              email: SESSION.user.email,
              role: 'owner',
              joinedAt: '2026-11-17T08:00:00Z',
            },
            {
              userId: '0190a000-0000-7000-8000-000000000004',
              displayName: 'Second Person',
              email: 'second.person@example.org',
              role: 'viewer',
              joinedAt: '2026-11-18T08:00:00Z',
            },
          ],
        });
      case `/api/v1/orgs/${ORG_ID}`:
        return route.fulfill({
          json: {
            id: ORG_ID,
            slug: 'example',
            name: 'Example Org',
            locale: 'en',
            timeZone: 'Europe/Zurich',
            requireMfa: false,
            aiPolicy: 'local_only',
            role: 'owner',
            createdAt: '2026-11-17T08:00:00Z',
            emailContent: 'full',
            allowMuteCritical: false,
          },
        });
      case `/api/v1/orgs/${ORG_ID}/setup`:
        return route.fulfill({
          json: { stepsDone: ['project'], firstProjectId: PROJECT.id, completedAt: null },
        });
      case `/api/v1/orgs/${ORG_ID}/projects`:
        return route.fulfill({ json: [PROJECT] });
      case `/api/v1/orgs/${ORG_ID}/projects/${PROJECT.id}`:
        return route.fulfill({ json: { project: PROJECT, components: COMPONENTS } });
      case `/api/v1/orgs/${ORG_ID}/projects/${PROJECT.id}/repositories`:
        return route.fulfill({ json: [LINK] });
      case `/api/v1/orgs/${ORG_ID}/projects/${PROJECT.id}/suggestions`:
        return route.fulfill({ json: [SUGGESTION] });
      case `/api/v1/orgs/${ORG_ID}/profile/coverage`:
        return route.fulfill({
          json: {
            components: 3,
            fullyCheckable: 1,
            hints: [
              {
                kind: 'version_line_only',
                severity: 'info',
                count: 1,
                projectIds: [PROJECT.id],
                examples: [
                  {
                    projectId: PROJECT.id,
                    componentId: COMPONENT.id,
                    name: 'ASP.NET Core',
                    detail: '10.0',
                  },
                ],
              },
              {
                kind: 'repositories_partial',
                severity: 'warning',
                count: 1,
                projectIds: [PROJECT.id],
                examples: [
                  {
                    projectId: PROJECT.id,
                    componentId: null,
                    name: 'example/portal',
                    detail: null,
                  },
                ],
              },
            ],
          },
        });
      case `/api/v1/orgs/${ORG_ID}/repository-connections`:
        return route.fulfill({ json: [CONNECTION] });
      case `/api/v1/orgs/${ORG_ID}/repository-connections/${CONNECTION.id}/repositories`:
        return route.fulfill({
          json: [
            {
              owner: 'example',
              name: 'portal',
              private: true,
              defaultBranch: 'main',
              pushedAt: null,
            },
          ],
        });
      case '/api/v1/github-hosts':
        return route.fulfill({ json: [] });
      case `/api/v1/orgs/${ORG_ID}/audit`:
        return route.fulfill({
          json: {
            items: [
              {
                id: '0190a000-0000-7000-8000-000000000020',
                at: '2026-11-18T08:00:00Z',
                action: 'project.created',
                subjectType: 'project',
                subjectId: PROJECT.id,
                data: { name: PROJECT.name },
                origin: 'ui',
                actorKind: 'member',
                actorName: 'Sample Person',
              },
            ],
            nextCursor: null,
          },
        });
      case '/api/v1/catalog/products':
        return route.fulfill({
          json: [
            {
              key: 'postgresql',
              name: 'PostgreSQL',
              vendor: 'PostgreSQL Global Development Group',
              kind: 'database',
              versionScheme: 'semver',
              platformKey: null,
            },
          ],
        });
      case `/api/v1/orgs/${ORG_ID}/invitations`:
        return route.fulfill({ json: [INVITATION] });
      case '/api/v1/invitations/sample-token':
        return route.fulfill({
          json: {
            organizationName: 'Example Org',
            email: INVITATION.email,
            role: 'member',
            invitedBy: SESSION.user.displayName,
            expiresAt: INVITATION.expiresAt,
          },
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
