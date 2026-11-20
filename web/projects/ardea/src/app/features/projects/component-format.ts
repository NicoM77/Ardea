import type { CatalogProductView, ComponentInput, ComponentView } from '../../core/api/client';

export const KIND_LABELS: Record<string, string> = {
  os: 'projects.kinds.os',
  runtime: 'projects.kinds.runtime',
  framework: 'projects.kinds.framework',
  cms: 'projects.kinds.cms',
  database: 'projects.kinds.database',
  messaging: 'projects.kinds.messaging',
  container: 'projects.kinds.container',
  network: 'projects.kinds.network',
  virtualization: 'projects.kinds.virtualization',
  library: 'projects.kinds.library',
  plugin: 'projects.kinds.plugin',
  service: 'projects.kinds.service',
  other: 'projects.kinds.other',
};

export const PACKAGING_LABELS: Record<string, string> = {
  upstream: 'projects.packaging.upstream',
  distribution: 'projects.packaging.distribution',
  unknown: 'projects.packaging.unknown',
};

export const EXPOSURES = ['internet', 'internal', 'isolated'] as const;
export const CRITICALITIES = ['high', 'normal', 'low'] as const;

export const EXPOSURE_LABELS: Record<string, string> = {
  internet: 'projects.exposure.internet',
  internal: 'projects.exposure.internal',
  isolated: 'projects.exposure.isolated',
};
export const EXPOSURE_HINTS: Record<string, string> = {
  internet: 'projects.exposure.internetHint',
  internal: 'projects.exposure.internalHint',
  isolated: 'projects.exposure.isolatedHint',
};
export const CRITICALITY_LABELS: Record<string, string> = {
  high: 'projects.criticality.high',
  normal: 'projects.criticality.normal',
  low: 'projects.criticality.low',
};
export const CRITICALITY_HINTS: Record<string, string> = {
  high: 'projects.criticality.highHint',
  normal: 'projects.criticality.normalHint',
  low: 'projects.criticality.lowHint',
};

export const CATALOG_GROUPS: readonly { id: string; label: string; kinds: readonly string[] }[] = [
  { id: 'os', label: 'projects.groups.os', kinds: ['os'] },
  { id: 'runtime', label: 'projects.groups.runtime', kinds: ['runtime', 'framework'] },
  { id: 'cms', label: 'projects.groups.cms', kinds: ['cms', 'plugin'] },
  { id: 'data', label: 'projects.groups.data', kinds: ['database', 'messaging'] },
  { id: 'containers', label: 'projects.groups.containers', kinds: ['container', 'virtualization'] },
  { id: 'network', label: 'projects.groups.network', kinds: ['network'] },
  { id: 'services', label: 'projects.groups.services', kinds: ['service'] },
  { id: 'other', label: 'projects.groups.other', kinds: ['library', 'other'] },
];

const CHECKABLE_ECOSYSTEMS = new Set([
  'npm',
  'nuget',
  'composer',
  'pypi',
  'maven',
  'golang',
]);

const ECOSYSTEM_NAMES: Record<string, string> = {
  npm: 'npm',
  nuget: 'NuGet',
  composer: 'Packagist',
  pypi: 'PyPI',
  maven: 'Maven',
  golang: 'Go',
  githubactions: 'GitHub Actions',
  gem: 'RubyGems',
  cargo: 'crates.io',
};

export function ecosystemOf(purl: string | null | undefined): string | null {
  const match = /^pkg:([a-z0-9.+-]+)\//i.exec(purl ?? '');
  return match ? match[1].toLowerCase() : null;
}

export function ecosystemName(id: string): string {
  return ECOSYSTEM_NAMES[id] ?? id;
}

export function isPackage(component: ComponentView): boolean {
  return component.kind === 'library' && !!ecosystemOf(component.purl);
}

export interface Ecosystem {
  id: string;
  name: string;
  packages: ComponentView[];
  direct: number;
  development: number;
  versionUnknown: number;
  checkable: boolean;
}

export function ecosystems(components: readonly ComponentView[]): Ecosystem[] {
  const groups = new Map<string, ComponentView[]>();
  for (const component of components) {
    if (!isPackage(component)) continue;
    const id = ecosystemOf(component.purl)!;
    groups.set(id, [...(groups.get(id) ?? []), component]);
  }
  return [...groups.entries()]
    .map(([id, packages]) => ({
      id,
      name: ecosystemName(id),
      packages: [...packages].sort((a, b) => a.name.localeCompare(b.name)),
      direct: packages.filter((p) => p.dependencyRelation === 'direct').length,
      development: packages.filter((p) => p.dependencyScope === 'development').length,
      versionUnknown: packages.filter((p) => !p.version).length,
      checkable: CHECKABLE_ECOSYSTEMS.has(id),
    }))
    .sort((a, b) => b.packages.length - a.packages.length || a.name.localeCompare(b.name));
}

export function platforms(components: readonly ComponentView[]): ComponentView[] {
  return components
    .filter((c) => !isPackage(c))
    .sort((a, b) => a.kind.localeCompare(b.kind) || a.name.localeCompare(b.name));
}

export interface PackageFilters {
  query: string;
  relation: '' | 'direct' | 'transitive';
  scope: '' | 'runtime' | 'development';
  versionUnknown: boolean;
}

export const NO_PACKAGE_FILTERS: PackageFilters = {
  query: '',
  relation: '',
  scope: '',
  versionUnknown: false,
};

export function filterPackages(
  packages: readonly ComponentView[],
  f: PackageFilters,
): ComponentView[] {
  const q = f.query.trim().toLowerCase();
  return packages.filter(
    (p) =>
      (!q || p.name.toLowerCase().includes(q)) &&
      (!f.relation || p.dependencyRelation === f.relation) &&
      (!f.scope || p.dependencyScope === f.scope) &&
      (!f.versionUnknown || !p.version),
  );
}

export interface DraftComponent {
  id: string | null;
  ref: string;
  productKey: string | null;
  name: string;
  vendor: string | null;
  kind: string;
  version: string;
  versionUnknown: boolean;
  packaging: string;
  protection: string;
  protectionNote: string;
  parentId: string | null;
  parentRef: string | null;
  platformKey: string | null;
}

let refs = 0;
const nextRef = () => `new-${++refs}`;

export function draftFromView(view: ComponentView): DraftComponent {
  return {
    id: view.id,
    ref: view.id,
    productKey: view.productKey,
    name: view.name,
    vendor: view.vendor,
    kind: view.kind,
    version: view.version ?? '',
    versionUnknown: !view.version,
    packaging: view.packaging,
    protection: view.protection,
    protectionNote: view.protectionNote ?? '',
    parentId: view.parentId,
    parentRef: null,
    platformKey: null,
  };
}

function draftFromProduct(
  product: CatalogProductView,
  existing: readonly DraftComponent[],
): DraftComponent {
  const parent = product.platformKey
    ? existing.find((d) => d.productKey === product.platformKey)
    : undefined;
  return {
    id: null,
    ref: nextRef(),
    productKey: product.key,
    name: product.name,
    vendor: product.vendor,
    kind: product.kind,
    version: '',
    versionUnknown: product.kind === 'service',
    packaging: 'unknown',
    protection: 'none',
    protectionNote: '',
    parentId: parent?.id ?? null,
    parentRef: parent && !parent.id ? parent.ref : null,
    platformKey: product.platformKey,
  };
}

export function draftFromName(name: string, kind: string): DraftComponent {
  return {
    id: null,
    ref: nextRef(),
    productKey: null,
    name: name.trim(),
    vendor: null,
    kind,
    version: '',
    versionUnknown: kind === 'service',
    packaging: 'unknown',
    protection: 'none',
    protectionNote: '',
    parentId: null,
    parentRef: null,
    platformKey: null,
  };
}

export function toInput(d: DraftComponent): ComponentInput {
  const version = d.versionUnknown || d.kind === 'service' ? null : d.version.trim() || null;
  return {
    id: d.id,
    ref: d.id ? null : d.ref,
    productKey: d.productKey,
    customVendor: d.productKey ? null : d.vendor,
    customName: d.productKey ? null : d.name,
    kind: d.productKey ? null : d.kind,
    version,
    packaging: d.packaging,
    parentId: d.parentId,
    parentRef: d.parentId ? null : d.parentRef,
    exposure: null,
    criticality: null,
    protection: d.protection,
    protectionNote: d.protection === 'effective' ? d.protectionNote.trim() || null : null,
  };
}

export function withoutDraft(drafts: readonly DraftComponent[], ref: string): DraftComponent[] {
  const gone = drafts.find((d) => d.ref === ref);
  if (!gone) return [...drafts];
  return drafts.filter(
    (d) => d.ref !== ref && !(gone.id && d.parentId === gone.id) && d.parentRef !== gone.ref,
  );
}

export function addChoice(
  drafts: readonly DraftComponent[],
  choice: { product: CatalogProductView } | { name: string; kind: string },
): { list: DraftComponent[]; draft: DraftComponent } {
  if ('product' in choice) {
    const existing = drafts.find((d) => d.productKey === choice.product.key);
    if (existing) return { list: [...drafts], draft: existing };
    const draft = draftFromProduct(choice.product, drafts);
    return { list: [...drafts, draft], draft };
  }
  const draft = draftFromName(choice.name, choice.kind);
  return { list: [...drafts, draft], draft };
}
