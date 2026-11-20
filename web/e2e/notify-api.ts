import type { Page, Route } from '@playwright/test';
import { api, ORG_ID, USER_ID } from './mock-api';

/**
 * Notifications for the flow tests of phase 5 (task 5.8), on top of `api()`: the three default rules, the
 * webhook targets and external addresses, the delivery log, the inbox with its counter, the member's own
 * settings, push devices, and the public link pages. The server keeps what a test changes, so a saved rule,
 * a new webhook or a read notification shows up when the page reads again.
 */
const HOUR = 3_600_000;
const at = (ms: number) => new Date(Date.now() + ms).toISOString();
let ids = 0;
const id = (prefix: string) => `0190a000-0000-7000-8${prefix}-${String(++ids).padStart(12, '0')}`;

type Json = Record<string, unknown>;

const target = (
  channel: string,
  ref: string,
  mode = 'immediate',
  cron: string | null = null,
  sendEmpty = false,
) => ({
  id: id('a01'),
  channel,
  recipientKind: 'role',
  recipientRef: ref,
  recipientLabel: ref,
  mode,
  digestCron: cron,
  sendEmpty,
  nextDigestAt: cron ? at(12 * HOUR) : null,
});

function defaultRules(): Json[] {
  const events = [
    'action.raised',
    'action.level_raised',
    'action.exploitation_escalated',
    'action.level_lowered',
    'action.withdrawn',
    'backfill.completed',
    'notice.raised',
  ];
  const rule = (
    name: string,
    preset: string,
    minLevel: number,
    ev: string[],
    targets: unknown[],
  ) => ({
    id: id('b01'),
    name,
    enabled: true,
    projectIds: [],
    minLevel,
    events: ev,
    preset,
    targets,
    warnings: [],
    createdAt: at(-48 * HOUR),
    changedAt: at(-48 * HOUR),
  });
  return [
    rule('Critical at once', 'critical', 5, events, [
      target('in_app', 'admin'),
      target('email', 'admin'),
      target('push', 'admin'),
    ]),
    rule('High', 'high', 4, events, [
      target('in_app', 'viewer'),
      target('email', 'viewer', 'digest', '0 8 * * 1-5'),
    ]),
    rule(
      'Weekly summary',
      'weekly',
      2,
      [...events, 'warning.published'],
      [target('email', 'viewer', 'digest', '0 8 * * 1', true)],
    ),
  ];
}

export interface NotifyState {
  rules: Json[];
  endpoints: Json[];
  notifications: Json[];
  devices: Json[];
  preferences: { mutedChannels: string[]; locale: string | null; canMuteCritical: boolean };
  /** The reminders by level (task 5a.6). */
  reminders: { level5: string; level4: string; level3: string; nextRunAt: string | null };
  /** The external address behind the link token `link-token`. */
  link: { state: string };
  requests: { method: string; path: string; body: unknown }[];
}

/**
 * `signedIn: false` is the visitor of a link page; `base: false` leaves the general answers to a stand-in
 * registered before (e.g. `flowsApi`), so that both serve one page.
 */
export async function notifyApi(
  page: Page,
  options: { signedIn?: boolean; base?: boolean } = {},
): Promise<NotifyState> {
  if (options.base !== false) await api(page, options.signedIn ?? true);
  const state: NotifyState = {
    rules: defaultRules(),
    endpoints: [],
    notifications: [
      {
        id: id('c01'),
        kind: 'action.level_raised',
        level: 5,
        title: '[5 Critical] #143 Puppet Enterprise in Platform Ops',
        body: 'Level 5, Critical. Raised from level 4.',
        link: '/o/example/actions/143',
        createdAt: at(-HOUR / 2),
        readAt: null,
      },
      {
        id: id('c01'),
        kind: 'warning.published',
        level: null,
        title: '[Warning] Phishing campaign against hospital suppliers',
        body: 'New warning without a vulnerability identifier.',
        link: '/entries/bacs-warnings%3A42',
        createdAt: at(-HOUR),
        readAt: null,
      },
      {
        id: id('c01'),
        kind: 'action.raised',
        level: 4,
        title: '[4 High] #141 WordPress in Webshop',
        body: 'Level 4, High. New action.',
        link: '/o/example/actions/141',
        createdAt: at(-30 * HOUR),
        readAt: at(-29 * HOUR),
      },
    ],
    devices: [],
    preferences: { mutedChannels: [], locale: null, canMuteCritical: false },
    reminders: { level5: 'daily', level4: 'weekly', level3: 'monthly', nextRunAt: at(20 * HOUR) },
    link: { state: 'pending' },
    requests: [],
  };
  const unread = () => state.notifications.filter((n) => !n.readAt).length;

  await page.route('**/api/v1/**', async (route: Route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    const method = request.method();
    const base = `/api/v1/orgs/${ORG_ID}`;
    const body = request.postData() ? (request.postDataJSON() as Json) : null;
    if (method !== 'GET') state.requests.push({ method, path: path + url.search, body });

    if (path === `${base}/notification-channels`) {
      return route.fulfill({
        json: ['in_app', 'email', 'webhook', 'push'].map((channel) => ({
          channel,
          available: true,
          reason: null,
        })),
      });
    }
    if (path === `${base}/rules/` || path === `${base}/rules`) {
      if (method === 'POST') {
        const rule = { ...ruleOf(body!), id: id('b01'), preset: null, createdAt: at(0) };
        state.rules.push(rule);
        return route.fulfill({ status: 201, json: rule });
      }
      return route.fulfill({ json: state.rules });
    }
    const ruleMatch = path.match(new RegExp(`^${base}/rules/([^/]+)$`));
    if (ruleMatch) {
      const index = state.rules.findIndex((r) => r.id === ruleMatch[1]);
      if (index < 0)
        return route.fulfill({ status: 404, json: { status: 404, code: 'rule_not_found' } });
      if (method === 'PUT') {
        state.rules[index] = { ...state.rules[index], ...ruleOf(body!) };
        return route.fulfill({ json: state.rules[index] });
      }
      if (method === 'DELETE') {
        state.rules.splice(index, 1);
        return route.fulfill({ status: 204 });
      }
      return route.fulfill({ json: state.rules[index] });
    }
    if (path === `${base}/endpoints/` || path === `${base}/endpoints`) {
      if (method === 'POST') {
        const endpoint = {
          id: id('d01'),
          name: body!['name'],
          host: new URL(String(body!['url'])).host,
          enabled: true,
          lastStatus: null,
          lastStatusAt: null,
          createdAt: at(0),
        };
        state.endpoints.push(endpoint);
        return route.fulfill({
          status: 201,
          json: { ...endpoint, secret: 'whsec_sample-secret-shown-once' },
        });
      }
      return route.fulfill({ json: state.endpoints });
    }
    const testMatch = path.match(new RegExp(`^${base}/endpoints/([^/]+)/test$`));
    if (testMatch) {
      const endpoint = state.endpoints.find((e) => e.id === testMatch[1])!;
      endpoint['lastStatus'] = '200';
      endpoint['lastStatusAt'] = at(0);
      return route.fulfill({ json: { ok: true, status: '200', error: null } });
    }
    if (path === `${base}/external-addresses/` || path === `${base}/external-addresses`) {
      return route.fulfill({ json: [] });
    }
    if (path === `${base}/deliveries`) {
      return route.fulfill({
        json: {
          items: [
            {
              id: id('e01'),
              kind: 'immediate',
              channel: 'email',
              recipient: { kind: 'user', id: USER_ID, label: 'Sample Person' },
              state: 'failed',
              attempts: 2,
              lastError: 'SocketException: Connection refused',
              events: 1,
              sendAfter: at(-HOUR),
              sentAt: null,
              createdAt: at(-HOUR),
              periodStart: null,
              periodEnd: null,
            },
          ],
          nextCursor: null,
        },
      });
    }
    if (path === `${base}/notifications/unread-count`)
      return route.fulfill({ json: { unread: unread() } });
    if (path === `${base}/notifications`) {
      const items =
        url.searchParams.get('unread') === 'true'
          ? state.notifications.filter((n) => !n.readAt)
          : state.notifications;
      return route.fulfill({ json: { items, nextCursor: null, unread: unread() } });
    }
    if (path === `${base}/notifications/read`) {
      const marked = body!['all']
        ? state.notifications.map((n) => n['id'])
        : (body!['ids'] as string[]);
      for (const n of state.notifications) if (marked.includes(n['id'])) n['readAt'] ??= at(0);
      return route.fulfill({ json: { unread: unread() } });
    }
    if (path === `${base}/reminder-rhythm/` || path === `${base}/reminder-rhythm`) {
      if (method === 'PUT') {
        state.reminders = {
          ...state.reminders,
          level5: String(body!['level5']),
          level4: String(body!['level4']),
          level3: String(body!['level3']),
        };
      }
      return route.fulfill({ json: state.reminders });
    }
    if (path === `${base}/notification-preferences`) {
      if (method === 'PUT') {
        state.preferences = {
          ...state.preferences,
          mutedChannels: body!['mutedChannels'] as string[],
          locale: body!['locale'] as string | null,
        };
      }
      return route.fulfill({ json: state.preferences });
    }
    if (path === '/api/v1/push/public-key') {
      // A valid uncompressed P-256 point (the example key of RFC 8291).
      return route.fulfill({
        json: {
          publicKey:
            'BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8',
        },
      });
    }
    if (path === '/api/v1/me/push-subscriptions/' || path === '/api/v1/me/push-subscriptions') {
      if (method === 'POST') {
        const device = {
          id: id('f01'),
          label: 'Chrome on Linux',
          createdAt: at(0),
          lastSuccessAt: null,
          failedCount: 0,
          endpoint: body!['endpoint'],
        };
        state.devices.push(device);
        return route.fulfill({ status: 201, json: device });
      }
      if (method === 'DELETE') {
        const endpoint = url.searchParams.get('endpoint');
        state.devices = state.devices.filter((d) => d['endpoint'] !== endpoint);
        return route.fulfill({ status: 204 });
      }
      return route.fulfill({ json: state.devices });
    }
    if (path === '/api/v1/notify/addresses/preview') {
      return body?.['token'] === 'link-token'
        ? route.fulfill({
            json: {
              organizationName: 'Example Org',
              address: 'support@agency.example',
              state: state.link.state,
            },
          })
        : route.fulfill({ status: 404, json: { status: 404, code: 'not_found' } });
    }
    if (path === '/api/v1/notify/addresses/confirm') {
      state.link.state = 'confirmed';
      return route.fulfill({ status: 204 });
    }
    if (path === '/api/v1/notify/addresses/unsubscribe') {
      state.link.state = 'unsubscribed';
      return route.fulfill({ status: 204 });
    }
    if (path === '/api/v1/notify/addresses/resubscribe') {
      state.link.state = 'confirmed';
      return route.fulfill({ status: 204 });
    }
    return route.fallback();
  });
  return state;
}

/** The rule as the API answers after a write: its targets with ids and labels. */
function ruleOf(request: Json): Json {
  const targets = (request['targets'] as Json[]).map((t) => ({
    ...t,
    id: id('a01'),
    recipientLabel: t['recipientKind'] === 'role' ? t['recipientRef'] : 'Sample Person',
    nextDigestAt: null,
  }));
  return { ...request, targets, warnings: [], changedAt: at(0) };
}
