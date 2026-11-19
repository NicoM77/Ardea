import type { CatalogProductView, ComponentView } from '../../core/api/client';
import {
  addChoice,
  draftFromName,
  draftFromView,
  ecosystemOf,
  ecosystems,
  filterPackages,
  NO_PACKAGE_FILTERS,
  platforms,
  toInput,
  withoutDraft,
} from './component-format';

const view = (extra: Partial<ComponentView>): ComponentView => ({
  id: 'c1',
  productKey: null,
  name: 'x',
  vendor: null,
  inCatalog: false,
  kind: 'library',
  version: '1.0.0',
  versionScheme: null,
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
  versionPrecision: 'exact',
  noticeThreshold: 'only_important',
  ...extra,
});

const product = (
  key: string,
  kind: string,
  platformKey: string | null = null,
): CatalogProductView => ({
  key,
  name: key,
  vendor: null,
  kind,
  versionScheme: 'semver',
  platformKey,
});

describe('packages and platforms', () => {
  const components = [
    view({
      id: 'a',
      name: '@angular/core',
      purl: 'pkg:npm/%40angular/core@22.2.1',
      dependencyRelation: 'direct',
      dependencyScope: 'runtime',
    }),
    view({
      id: 'b',
      name: 'esbuild',
      purl: 'pkg:npm/esbuild@0.25.9',
      dependencyRelation: 'transitive',
      dependencyScope: 'development',
    }),
    view({ id: 'c', name: 'DbUp', purl: 'pkg:nuget/DbUp', version: null }),
    view({ id: 'd', name: 'actions/checkout', purl: 'pkg:githubactions/actions/checkout@v5' }),
    view({ id: 'e', name: 'PostgreSQL', kind: 'database', purl: null, origin: 'manual' }),
  ];

  it('reads the ecosystem from a package URL', () => {
    expect(ecosystemOf('pkg:npm/%40angular/core@22.2.1')).toBe('npm');
    expect(ecosystemOf('pkg:NuGet/DbUp')).toBe('nuget');
    expect(ecosystemOf(null)).toBeNull();
  });

  it('sums each ecosystem and knows which ones can be checked', () => {
    const list = ecosystems(components);
    expect(list.map((e) => [e.name, e.packages.length, e.checkable])).toEqual([
      ['npm', 2, true],
      ['GitHub Actions', 1, false],
      ['NuGet', 1, true],
    ]);
    expect(list[0]).toMatchObject({ direct: 1, development: 1, versionUnknown: 0 });
    expect(list[2].versionUnknown).toBe(1);
    expect(platforms(components).map((c) => c.name)).toEqual(['PostgreSQL']);
  });

  it('filters by name, relation, use and missing version', () => {
    const npm = ecosystems(components)[0].packages;
    expect(filterPackages(npm, { ...NO_PACKAGE_FILTERS, query: 'ESB' }).map((p) => p.id)).toEqual([
      'b',
    ]);
    expect(
      filterPackages(npm, { ...NO_PACKAGE_FILTERS, relation: 'direct' }).map((p) => p.id),
    ).toEqual(['a']);
    expect(
      filterPackages(npm, { ...NO_PACKAGE_FILTERS, scope: 'development' }).map((p) => p.id),
    ).toEqual(['b']);
    expect(
      filterPackages(components, { ...NO_PACKAGE_FILTERS, versionUnknown: true }).map((p) => p.id),
    ).toEqual(['c']);
  });
});

describe('drafts for PUT …/components', () => {
  it('keeps the id of a saved component and sends a ref for a new one', () => {
    const saved = draftFromView(
      view({ id: 'c9', productKey: 'php', kind: 'runtime', origin: 'manual', version: '8.3.30' }),
    );
    expect(toInput(saved)).toMatchObject({
      id: 'c9',
      ref: null,
      productKey: 'php',
      version: '8.3.30',
      customName: null,
    });

    const free = { ...draftFromName('Contoso Forms', 'plugin'), version: '3.2' };
    expect(toInput(free)).toMatchObject({
      id: null,
      productKey: null,
      customName: 'Contoso Forms',
      kind: 'plugin',
      version: '3.2',
    });
    expect(toInput(free).ref).toMatch(/^new-/);
  });

  it('sends no version when it is unknown, and none for a service', () => {
    const unknown = { ...draftFromName('Thing', 'other'), version: '1.0', versionUnknown: true };
    expect(toInput(unknown).version).toBeNull();
    const service = { ...draftFromName('Cloudflare', 'service'), version: '1' };
    expect(toInput(service).version).toBeNull();
  });

  it('puts a plugin under its platform by ref, adds a product once, and removes plugins with their platform', () => {
    let { list } = addChoice([], { product: product('wordpress', 'cms') });
    const platform = list[0];
    ({ list } = addChoice(list, { product: product('woocommerce', 'plugin', 'wordpress') }));
    expect(toInput(list[1])).toMatchObject({ parentRef: platform.ref, parentId: null });

    expect(addChoice(list, { product: product('wordpress', 'cms') }).list).toHaveLength(2);
    expect(withoutDraft(list, platform.ref)).toEqual([]);
  });

  it('puts a plugin under a saved platform by id', () => {
    const saved = draftFromView(
      view({ id: 'wp', productKey: 'wordpress', kind: 'cms', origin: 'manual' }),
    );
    const { draft } = addChoice([saved], {
      product: product('woocommerce', 'plugin', 'wordpress'),
    });
    expect(toInput(draft)).toMatchObject({ parentId: 'wp', parentRef: null });
  });
});
