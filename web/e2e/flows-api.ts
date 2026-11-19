import type { Page, Route } from '@playwright/test';
import { api, ORG_ID, PROJECT_ID, USER_ID } from './mock-api';

/**
 * The organization's actions for the flow tests of phase 3 (overview, actions, action, alert), on
 * top of `api()`. Times are relative to now, so deadlines always lie ahead. The server keeps the
 * state of the actions per test, checks `expectedStatus` like the API and answers 409
 * `status_conflict` when it does not match.
 */
const PLATFORM = { id: '0190a000-0000-7000-8000-000000000030', name: 'Platform Ops' };
const PORTAL = { id: PROJECT_ID, name: 'Customer Portal' };
const HOUR = 3_600_000;
const DAY = 24 * HOUR;
const at = (ms: number) => new Date(Date.now() + ms).toISOString();

interface Row {
  number: number;
  level: number;
  levelName: string;
  status: string;
  project: { id: string; name: string };
  component: { id: string; name: string; version: string | null; removed: boolean };
  targetVersion: string | null;
  openFindings: number;
  exploitation: string;
  fixAvailable: boolean;
  assignedTo: string | null;
  dueAt: string | null;
  overdue: boolean;
  openedAt: string;
  levelRaisedAt: string | null;
  closedAt: string | null;
  closeReason: string | null;
  isBackfill: boolean;
  entries: string[];
}

function row(extra: Partial<Row> & Pick<Row, 'number' | 'level' | 'levelName'>): Row {
  return {
    status: 'open',
    project: PLATFORM,
    component: { id: `c-${extra.number}`, name: 'Component', version: '1.0', removed: false },
    targetVersion: null,
    openFindings: 1,
    exploitation: 'none',
    fixAvailable: true,
    assignedTo: null,
    dueAt: at(7 * DAY),
    overdue: false,
    openedAt: at(-DAY),
    levelRaisedAt: null,
    closedAt: null,
    closeReason: null,
    isBackfill: false,
    entries: [],
    ...extra,
  };
}

function actions(): Row[] {
  return [
    row({
      number: 141,
      level: 5,
      levelName: 'critical',
      component: { id: 'c-141', name: 'Puppet Enterprise', version: '2025.9.0', removed: false },
      targetVersion: '2025.12.1',
      exploitation: 'active',
      dueAt: at(DAY + 13 * HOUR),
      openedAt: at(-5 * DAY),
      entries: ['CVE-2026-115731'],
    }),
    row({
      number: 143,
      level: 4,
      levelName: 'high',
      component: { id: 'c-143', name: 'PostgreSQL', version: '17.9', removed: false },
      targetVersion: '17.12',
      openFindings: 2,
      dueAt: at(6 * DAY),
      openedAt: at(-DAY),
      entries: ['CVE-2026-116325', 'CVE-2026-118402'],
    }),
    row({
      number: 150,
      level: 3,
      levelName: 'medium',
      status: 'in_review',
      project: PORTAL,
      component: { id: 'c-150', name: 'Umbraco CMS', version: '17.5', removed: false },
      targetVersion: '17.7.1',
      assignedTo: USER_ID,
      dueAt: at(28 * DAY),
      openedAt: at(-2 * DAY),
      entries: ['CVE-2026-130068'],
    }),
    row({
      number: 152,
      level: 2,
      levelName: 'low',
      component: { id: 'c-152', name: 'ingress-nginx', version: '1.13.2', removed: false },
      fixAvailable: false,
      dueAt: at(84 * DAY),
      openedAt: at(-6 * DAY),
      entries: ['CVE-2026-116472'],
    }),
    row({
      number: 120,
      level: 4,
      levelName: 'high',
      status: 'closed',
      closeReason: 'remediated',
      project: PORTAL,
      component: { id: 'c-120', name: '.NET runtime', version: '10.0.3', removed: false },
      targetVersion: '10.0.4',
      openFindings: 0,
      assignedTo: USER_ID,
      dueAt: at(-DAY),
      openedAt: at(-8 * DAY),
      closedAt: at(-7 * DAY),
      entries: ['CVE-2026-100001'],
    }),
  ];
}

const POSTGRES_ANSWERS = {
  origin: 'template',
  whatHappened: {
    entryKey: 'CVE-2026-116325',
    title: 'PostgreSQL: function owner’s rights through a crafted search path',
    excerpt:
      'A database user who may create objects can make PostgreSQL run their SQL with the rights of a function’s owner, through a crafted search path. Three further flaws in the same release are minor.',
    source: 'CVE Program',
    moreEntries: 1,
  },
  affected: {
    verdict: 'affected',
    items: [
      {
        project: PLATFORM,
        component: { id: 'c-143', name: 'PostgreSQL', version: '17.9', removed: false },
        level: 4,
        versionVerdict: 'in_range',
        versionReason: 'in_affected_range',
        ranges: [{ introduced: '17', fixed: '17.12', lastAffected: null, exact: null }],
      },
    ],
    moreItems: 0,
  },
  toDo: {
    kind: 'update',
    targetVersion: '17.12',
    installedVersion: '17.9',
    steps: [],
    advisoryUrl: 'https://www.postgresql.org/support/security/',
  },
};

const FACTORS = {
  rulesetVersion: 1,
  level: 4,
  factors: [
    { factor: 'severity', value: 'high', evidence: 'CVSS 3.1: 8.8', points: 3 },
    { factor: 'exploitation', value: 'none', points: 0 },
    {
      factor: 'affectedness',
      value: 'confirmed',
      evidence: 'version 17.9 in an affected range',
      points: 1,
    },
    { factor: 'criticality', value: 'high', points: 1 },
    { factor: 'exposure', value: 'internal', points: 0 },
    { factor: 'protection', value: 'none', points: 0 },
    { factor: 'authorityWarning', value: 'no', points: 0 },
    { factor: 'epss', value: 'low', points: 0 },
  ],
  points: 5,
  pointsLevel: 4,
  caps: [],
  floors: [],
};

function finding(
  id: string,
  key: string,
  title: string,
  levelName: string,
  level: number,
  status: string,
) {
  return {
    id,
    alertNumber: 142,
    actionNumber: 143,
    entry: { id: `e-${id}`, key, title },
    project: PLATFORM,
    component: { id: 'c-143', name: 'PostgreSQL', version: '17.9', removed: false },
    level,
    levelName,
    status,
    statusReason: null,
    statusNote: null,
    statusChangedBy: null,
    statusChangedAt: at(-DAY),
    matchType: 'product',
    versionVerdict: 'in_range',
    versionReason: 'in_affected_range',
    fixedVersion: '17.12',
    verdict: 'affected',
    verdictSource: 'rules',
    exploitation: 'none',
    fixAvailable: true,
    isBackfill: false,
    factors: FACTORS,
    evidence: {},
  };
}

export interface FlowOptions {
  /** A quiet day: no critical or high action open. */
  calm?: boolean;
  /** Someone else takes the action into review just before the first status change. */
  conflict?: boolean;
}

export async function flowsApi(page: Page, options: FlowOptions = {}): Promise<void> {
  await api(page, true);
  let rows = actions();
  if (options.calm) rows = rows.filter((r) => r.level <= 3);
  let conflict = !!options.conflict;
  const events: {
    id: string;
    kind: string;
    at: string;
    actor: string | null;
    findingId: string | null;
    data: unknown;
  }[] = [
    {
      id: '0193f2a1-5b8e-7c05-b1f2-7a6d4c9e2f31',
      kind: 'action.raised',
      at: at(-DAY),
      actor: null,
      findingId: null,
      data: { level: 4, findings: 2, exploitation: 'none' },
    },
  ];

  const open = () => rows.filter((r) => r.status !== 'closed');
  const detail = (number: number) => {
    const action = rows.find((r) => r.number === number)!;
    const findingStatus =
      action.status === 'closed' ? (action.closeReason ?? 'remediated') : action.status;
    return {
      action,
      answers: POSTGRES_ANSWERS,
      findings: [
        finding(
          'f1',
          'CVE-2026-116325',
          'Function owner’s rights through a crafted search path',
          'high',
          4,
          findingStatus,
        ),
        finding(
          'f2',
          'CVE-2026-118402',
          'Timing leak in SCRAM authentication',
          'low',
          2,
          findingStatus,
        ),
      ],
      events,
    };
  };

  await page.route('**/api/v1/**', async (route: Route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    const base = `/api/v1/orgs/${ORG_ID}`;
    const q = url.searchParams;

    if (path === `${base}/overview`) {
      const levels = [5, 4, 3, 2].map((level) => ({
        level,
        levelName: { 5: 'critical', 4: 'high', 3: 'medium', 2: 'low' }[level],
        actions: open().filter((r) => r.level === level).length,
      }));
      const urgent = [...open()].sort((a, b) => b.level - a.level).slice(0, 5);
      return route.fulfill({
        json: {
          openByLevel: levels,
          overdue: 0,
          urgent: urgent.map((action) => ({ action, answers: POSTGRES_ANSWERS })),
          projects: [PLATFORM, PORTAL]
            .map((project) => {
              const mine = open().filter((r) => r.project.id === project.id);
              const level = Math.max(0, ...mine.map((r) => r.level));
              return { project, level, levelName: 'high', actions: mine.length };
            })
            .filter((p) => p.actions > 0),
          last24Hours: [
            { kind: 'action.raised', count: 2 },
            { kind: 'action.status_changed', count: 1 },
          ],
        },
      });
    }
    if (path === `${base}/actions` && request.method() === 'GET') {
      const status = q.get('status') ?? 'active';
      const levels = (q.get('level') ?? '').split(',').filter(Boolean).map(Number);
      const project = q.get('project');
      const words = (q.get('q') ?? '').toLowerCase();
      const items = rows
        .filter((r) => (status === 'closed' ? r.status === 'closed' : r.status !== 'closed'))
        .filter((r) => !levels.length || levels.includes(r.level))
        .filter((r) => !project || r.project.id === project)
        .filter(
          (r) =>
            !words ||
            [r.component.name, r.project.name, ...r.entries].some((t) =>
              t.toLowerCase().includes(words),
            ),
        )
        .sort((a, b) => b.level - a.level || b.number - a.number);
      return route.fulfill({ json: { items, nextCursor: null } });
    }
    const one = new RegExp(`^${base}/actions/(\\d+)(/status|/notes)?$`).exec(path);
    if (one) {
      const number = Number(one[1]);
      const action = rows.find((r) => r.number === number);
      if (!action)
        return route.fulfill({ status: 404, json: { status: 404, code: 'action_not_found' } });
      if (one[2] === '/status') {
        const body = request.postDataJSON() as {
          status: string;
          expectedStatus: string;
          note: string | null;
        };
        if (conflict) {
          conflict = false;
          action.status = 'in_review';
          action.assignedTo = '0190a000-0000-7000-8000-000000000004';
        }
        if (action.status !== body.expectedStatus) {
          return route.fulfill({
            status: 409,
            contentType: 'application/problem+json',
            body: JSON.stringify({
              status: 409,
              code: 'status_conflict',
              title: `The action is ${action.status} by now.`,
            }),
          });
        }
        const outcome = ['remediated', 'risk_accepted', 'not_affected'].includes(body.status);
        events.push({
          id: `0193f2a1-0000-7000-8000-${String(events.length).padStart(12, '0')}`,
          kind: 'action.status_changed',
          at: at(0),
          actor: USER_ID,
          findingId: null,
          data: { from: action.status, to: body.status, note: body.note },
        });
        action.status = outcome ? 'closed' : body.status;
        action.closeReason = outcome ? body.status : null;
        action.closedAt = outcome ? at(0) : null;
        return route.fulfill({ json: detail(number) });
      }
      if (one[2] === '/notes') return route.fulfill({ status: 201, json: events[0] });
      if (request.method() === 'PATCH') {
        const body = request.postDataJSON() as {
          assignedTo: string | null;
          unassign: boolean | null;
          dueAt: string | null;
        };
        if (body.assignedTo) action.assignedTo = body.assignedTo;
        if (body.unassign) action.assignedTo = null;
        if (body.dueAt) action.dueAt = body.dueAt;
        return route.fulfill({ json: detail(number) });
      }
      return route.fulfill({ json: detail(number) });
    }
    if (path === `${base}/alerts/142`) {
      const d = detail(143);
      return route.fulfill({
        json: {
          alert: {
            number: 142,
            level: 4,
            levelName: 'high',
            entry: { id: 'e-f1', key: 'CVE-2026-116325', title: d.findings[0].entry.title },
            openActions: 1,
            findings: 1,
            firstRaisedAt: at(-DAY),
            changedAt: at(-DAY),
          },
          entry: {
            id: 'e-f1',
            key: 'CVE-2026-116325',
            title: 'PostgreSQL: function owner’s rights through a crafted search path',
            state: 'published',
            publishedAt: at(-4 * DAY),
            changedAt: at(-DAY),
            cvssScore: 8.8,
            cvssVersion: '3.1',
            cvssVector: 'CVSS:3.1/AV:N/AC:L/PR:L/UI:N/S:U/C:H/I:H/A:H',
            exploitation: 'none',
            exploitationSource: null,
            exploitationSince: null,
            fixAvailable: true,
            epssClass: 'low',
            authorityWarning: false,
          },
          answers: POSTGRES_ANSWERS,
          actions: [d.action],
          findings: [d.findings[0]],
        },
      });
    }
    if (path === '/api/v1/entries' && q.get('kind') === 'warning') {
      return route.fulfill({
        json: {
          items: [
            {
              key: 'bacs-warnings:42',
              kind: 'warning',
              title: 'Phishing campaign against hospital suppliers',
              state: 'published',
              cvssScore: null,
              severity: 'unrated',
              exploitation: 'none',
              epssClass: null,
              fixAvailable: false,
              authorityWarning: true,
              publishedAt: at(-2 * DAY),
              changedAt: at(-2 * DAY),
              aliases: [],
            },
          ],
          nextCursor: null,
        },
      });
    }
    if (path === '/api/v1/status') return route.fulfill({ json: [] });
    return route.fallback();
  });
}
