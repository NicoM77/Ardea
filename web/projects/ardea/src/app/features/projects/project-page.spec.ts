import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { computed, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { of } from 'rxjs';
import type { ComponentView, LinkView, SuggestionView } from '../../core/api/client';
import { Session, type Me } from '../../core/session/session';
import { ProjectPage } from './project-page';

const ORG = '0190a000-0000-7000-8000-0000000000aa';
const PROJECT = '0190a000-0000-7000-8000-0000000000bb';
const BASE = `/api/v1/orgs/${ORG}/projects/${PROJECT}`;

async function settle(fixture: ComponentFixture<unknown>): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve));
  TestBed.tick();
  fixture.detectChanges();
}

const component = (extra: Partial<ComponentView>): ComponentView => ({
  id: 'c',
  productKey: null,
  name: 'x',
  vendor: null,
  inCatalog: true,
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

const link = (extra: Partial<LinkView>): LinkView => ({
  id: 'l1',
  connectionId: 'k1',
  connectionName: 'Example on GitHub',
  owner: 'example',
  name: 'portal',
  ref: null,
  path: null,
  state: 'ok',
  stateReason: null,
  failingSince: null,
  lastCommitSha: '4f2a9c1aa',
  lastIndexedAt: '2026-11-19T08:00:00Z',
  nextCheckAt: '2026-11-19T09:00:00Z',
  shrinkPending: false,
  componentCount: 2,
  ...extra,
});

const suggestion: SuggestionView = {
  id: 's1',
  linkId: 'l1',
  productKey: 'aspnetcore',
  productName: 'ASP.NET Core',
  version: '10.0',
  evidence: [{ file: 'Dockerfile', line: 14, value: 'FROM mcr.microsoft.com/dotnet/aspnet:10.0' }],
  state: 'open',
  componentId: null,
};

describe('ProjectPage', () => {
  async function render(links: LinkView[]) {
    const user = signal<Me>({
      id: 'me',
      email: 'me@example.org',
      displayName: 'Me',
      isInstanceAdmin: false,
      twoFactorEnabled: true,
      organizations: [
        { id: ORG, slug: 'heron', name: 'Heron', role: 'member', mfaRequired: false },
      ],
    });
    const params = convertToParamMap({ slug: 'heron', projectId: PROJECT });
    TestBed.configureTestingModule({
      imports: [ProjectPage, TranslocoTestingModule.forRoot({ langs: { en: {} } })],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: Session, useValue: { user: computed(() => user()) } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: params }, paramMap: of(params), parent: null },
        },
      ],
    });
    const fixture = TestBed.createComponent(ProjectPage);
    fixture.componentRef.setInput('projectId', PROJECT);
    await settle(fixture);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne(BASE).flush({
      project: {
        id: PROJECT,
        name: 'Customer Portal',
        description: 'Self-service',
        criticality: 'high',
        exposure: 'internet',
        archived: false,
        componentCount: 3,
        createdAt: '2026-11-19T08:00:00Z',
        changedAt: '2026-11-19T08:00:00Z',
      },
      components: [
        component({
          id: 'p1',
          name: 'Umbraco CMS',
          kind: 'cms',
          productKey: 'umbraco',
          version: '16.3.2',
        }),
        component({
          id: 'p2',
          name: 'Azure SQL Database',
          kind: 'service',
          version: null,
          origin: 'manual',
        }),
        component({
          id: 'k1',
          name: 'rxjs',
          purl: 'pkg:npm/rxjs@7.8.2',
          dependencyRelation: 'direct',
          dependencyScope: 'runtime',
        }),
      ],
    });
    http.expectOne(`${BASE}/repositories`).flush(links);
    http.expectOne((r) => r.url === `${BASE}/suggestions`).flush([suggestion]);
    http
      .expectOne((r) => r.url === `/api/v1/orgs/${ORG}/profile/coverage`)
      .flush({
        components: 3,
        fullyCheckable: 2,
        hints: [
          {
            kind: 'missing_version',
            severity: 'warning',
            count: 1,
            projectIds: [PROJECT],
            examples: [],
          },
        ],
      });
    await settle(fixture);
    return { fixture, http, element: fixture.nativeElement as HTMLElement };
  }

  const button = (root: Element | null, text: string) =>
    [...(root?.querySelectorAll<HTMLButtonElement>('button') ?? [])].find(
      (b) => b.textContent?.trim() === text,
    );

  it('groups platforms and services apart from packages, with their origin', async () => {
    const { element } = await render([link({})]);
    expect(element.querySelector('ardea-coverage-card')?.textContent).toContain(
      'en.projects.coverage.kinds.missingVersion',
    );
    const platforms = element.querySelector('table[aria-label="en.projects.platforms"]')!;
    expect(platforms.textContent).toContain('Umbraco CMS');
    expect(platforms.textContent).toContain('en.projects.noVersion');
    expect(platforms.textContent).not.toContain('rxjs');
    expect(element.querySelector('ardea-packages-view')?.textContent).toContain('rxjs');
    expect(element.querySelector('ardea-links-card')?.textContent).toContain('example/portal');
  });

  it('detaches a component from its repository', async () => {
    const { fixture, http, element } = await render([link({})]);
    element
      .querySelector<HTMLButtonElement>('button[aria-label="en.projects.detachNamed"]')!
      .click();
    await settle(fixture);
    const detach = http.expectOne(`${BASE}/components/p1/detach`);
    expect(detach.request.method).toBe('POST');
    detach.flush(component({ id: 'p1', origin: 'manual' }));
    await settle(fixture);
    http.expectOne(BASE).flush({ project: null, components: [] });
    http.expectOne((r) => r.url === `/api/v1/orgs/${ORG}/profile/coverage`);
    expect(element.textContent).toContain('en.projects.detached');
  });

  it('confirms a suggestion with a corrected version', async () => {
    const { fixture, http, element } = await render([link({})]);
    const list = element.querySelector('ardea-suggestions-list');
    expect(list?.textContent).toContain('FROM mcr.microsoft.com/dotnet/aspnet:10.0');
    button(list, 'en.projects.suggestions.change')!.click();
    await settle(fixture);
    const input = list!.querySelector<HTMLInputElement>('input')!;
    input.value = '10.0.1';
    input.dispatchEvent(new Event('input'));
    button(list, 'en.projects.suggestions.saveConfirm')!.click();
    await settle(fixture);
    const decide = http.expectOne(`${BASE}/suggestions/s1`);
    expect(decide.request.body).toEqual({ decision: 'accept', version: '10.0.1' });
  });

  it('asks again while a repository is read', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    try {
      const { fixture, http } = await render([
        link({ state: 'pending', lastCommitSha: null, lastIndexedAt: null }),
      ]);
      vi.advanceTimersByTime(4100);
      await settle(fixture);
      http.expectOne(`${BASE}/repositories`).flush([link({})]);
      await settle(fixture);
      // Done reading: the project and its suggestions are loaded again.
      http.expectOne(BASE);
      http.expectOne((r) => r.url === `${BASE}/suggestions`);
      http.expectOne((r) => r.url === `/api/v1/orgs/${ORG}/profile/coverage`);
    } finally {
      vi.useRealTimers();
    }
  });
});
