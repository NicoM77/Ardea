import type { Page, Route } from '@playwright/test';
import { ORG_ID } from './mock-api';

const STACK_ID = '0190a000-0000-7000-8000-0000000000f0';

interface Product {
  key: string;
  name: string;
  vendor: string;
  kind: string;
  aliases: string[];
}

const CATALOGUE: Product[] = [
  {
    key: 'postgresql',
    name: 'PostgreSQL',
    vendor: 'PostgreSQL',
    kind: 'database',
    aliases: ['postgres'],
  },
  {
    key: 'iis',
    name: 'Internet Information Services',
    vendor: 'Microsoft',
    kind: 'network',
    aliases: ['iis'],
  },
  { key: 'windows-server', name: 'Windows Server', vendor: 'Microsoft', kind: 'os', aliases: [] },
  {
    key: 'exchange-server',
    name: 'Microsoft Exchange Server',
    vendor: 'Microsoft',
    kind: 'other',
    aliases: ['exchange'],
  },
  {
    key: 'azure-devops-server',
    name: 'Azure DevOps Server',
    vendor: 'Microsoft',
    kind: 'other',
    aliases: [],
  },
  { key: 'nginx', name: 'nginx', vendor: 'F5', kind: 'network', aliases: [] },
  { key: 'ubuntu', name: 'Ubuntu', vendor: 'Canonical', kind: 'os', aliases: ['ubuntu linux'] },
  { key: 'linux-kernel', name: 'Linux kernel', vendor: 'Linux', kind: 'os', aliases: ['linux'] },
  { key: 'redis', name: 'Redis', vendor: 'Redis', kind: 'database', aliases: [] },
  { key: 'angular', name: 'Angular', vendor: 'Google', kind: 'framework', aliases: [] },
  {
    key: 'docker-engine',
    name: 'Docker Engine',
    vendor: 'Docker',
    kind: 'container',
    aliases: ['docker'],
  },
  { key: 'fortios', name: 'FortiOS', vendor: 'Fortinet', kind: 'network', aliases: ['fortigate'] },
];
const COMMON = [
  'angular',
  'postgresql',
  'redis',
  'ubuntu',
  'nginx',
  'docker-engine',
  'fortios',
  'iis',
];
const COMPANIONS: Record<string, string> = {
  iis: 'windows-server',
  'exchange-server': 'windows-server',
  ubuntu: 'linux-kernel',
};
const NAMED: Record<string, string[]> = { redis: ['Platform Ops'] };

const product = (key: string) => CATALOGUE.find((p) => p.key === key)!;

function entry(key: string, version: string | null, precision: string, threshold: string) {
  const p = product(key);
  return {
    id: `0190a000-0000-7000-8000-${String(Math.floor(Math.random() * 1e12)).padStart(12, '0')}`,
    productKey: p.key,
    name: p.name,
    vendor: p.vendor,
    inCatalog: true,
    kind: p.kind,
    version,
    versionScheme: 'semver',
    packaging: 'unknown',
    parentId: null,
    exposure: null,
    criticality: null,
    protection: 'none',
    protectionNote: null,
    origin: 'manual',
    purl: null,
    dependencyScope: null,
    dependencyRelation: null,
    versionPrecision: precision,
    noticeThreshold: threshold,
  };
}

const derived = (version: string | null) =>
  !version ? 'none' : /^(\d+|\d+(\.\d+)*\.[xX*])$/.test(version) ? 'line' : 'exact';

export interface StackState {
  writes: { method: string; path: string; body: unknown }[];
  failWrites: boolean;
}

export async function stackApi(
  page: Page,
  options: { wizard?: boolean } = {},
): Promise<StackState> {
  const state: StackState = { writes: [], failWrites: false };
  let entries = options.wizard
    ? []
    : [
        entry('ubuntu', null, 'none', 'only_important'),
        entry('postgresql', '17', 'line', 'only_important'),
      ];
  const setup = options.wizard
    ? { stepsDone: [] as string[], firstProjectId: null, completedAt: null as string | null }
    : {
        stepsDone: ['stack', 'project', 'repository', 'operations', 'technologies', 'coverage'],
        firstProjectId: null,
        completedAt: '2026-11-19T09:00:00Z' as string | null,
      };

  const suggestions = () => {
    const taken = new Set(entries.map((e) => e.productKey));
    const out: unknown[] = [];
    const add = (key: string, reason: string, basis: string | null, projects: string[]) => {
      if (taken.has(key)) return;
      taken.add(key);
      const p = product(key);
      out.push({ productKey: key, name: p.name, kind: p.kind, reason, basis, projects });
    };
    for (const e of [...entries].sort((a, b) => a.productKey.localeCompare(b.productKey)))
      if (COMPANIONS[e.productKey]) add(COMPANIONS[e.productKey], 'companion', e.name, []);
    for (const [key, projects] of Object.entries(NAMED)) add(key, 'project', null, projects);
    for (const key of COMMON) add(key, 'common', null, []);
    return out;
  };

  await page.route('**/api/v1/**', async (route: Route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    const base = `/api/v1/orgs/${ORG_ID}`;
    const components = `${base}/projects/${STACK_ID}/components`;

    if (path === '/api/v1/catalog/products') {
      const q = (url.searchParams.get('q') ?? '').toLowerCase();
      const found = CATALOGUE.filter(
        (p) => p.name.toLowerCase().includes(q) || p.aliases.some((a) => a.startsWith(q)),
      );
      return route.fulfill({
        json: found.map((p) => ({
          key: p.key,
          name: p.name,
          vendor: p.vendor,
          kind: p.kind,
          versionScheme: 'semver',
          platformKey: null,
        })),
      });
    }
    if (path === `${base}/stack`)
      return route.fulfill({ json: { projectId: STACK_ID, components: entries } });
    if (path === `${base}/stack/suggestions`) return route.fulfill({ json: suggestions() });
    if (path === `${base}/setup`) {
      if (request.method() === 'PATCH') {
        const body = request.postDataJSON() as { stepDone: string | null };
        if (body.stepDone && !setup.stepsDone.includes(body.stepDone))
          setup.stepsDone.push(body.stepDone);
      }
      return route.fulfill({ json: setup });
    }
    if (path === `${base}/setup/complete`) {
      setup.completedAt ??= new Date().toISOString();
      return route.fulfill({ json: setup });
    }
    if (state.failWrites && path.startsWith(components) && request.method() !== 'GET')
      return route.fulfill({ status: 409, json: { status: 409, code: 'write_refused' } });
    if (path === components && request.method() === 'POST') {
      const body = request.postDataJSON() as {
        productKey: string;
        version: string | null;
        versionPrecision?: string | null;
        noticeThreshold?: string | null;
      };
      state.writes.push({ method: 'POST', path, body });
      const added = entry(
        body.productKey,
        body.version,
        body.versionPrecision ?? derived(body.version),
        body.noticeThreshold ?? 'only_important',
      );
      entries = [...entries, added];
      return route.fulfill({ status: 201, json: added });
    }
    if (path.startsWith(`${components}/`)) {
      const id = path.slice(components.length + 1);
      const current = entries.find((e) => e.id === id);
      if (!current)
        return route.fulfill({ status: 404, json: { status: 404, code: 'component_not_found' } });
      if (request.method() === 'DELETE') {
        state.writes.push({ method: 'DELETE', path, body: null });
        entries = entries.filter((e) => e.id !== id);
        return route.fulfill({ status: 204 });
      }
      if (request.method() === 'PATCH') {
        const body = request.postDataJSON() as {
          version: string | null;
          versionPrecision: string | null;
          noticeThreshold: string | null;
        };
        state.writes.push({ method: 'PATCH', path, body });
        const version = body.version === null ? current.version : body.version.trim() || null;
        const changed = {
          ...current,
          version,
          versionPrecision:
            body.versionPrecision ??
            (body.version === null ? current.versionPrecision : derived(version)),
          noticeThreshold: body.noticeThreshold ?? current.noticeThreshold,
        };
        entries = entries.map((e) => (e.id === id ? changed : e));
        return route.fulfill({ json: changed });
      }
    }
    return route.fallback();
  });
  return state;
}
