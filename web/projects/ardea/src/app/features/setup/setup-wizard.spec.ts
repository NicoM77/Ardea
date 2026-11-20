import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { computed, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { of } from 'rxjs';
import type { ComponentView, ProjectView } from '../../core/api/client';
import { Session, type Me } from '../../core/session/session';
import { SetupWizard } from './setup-wizard';

const ORG = '0190a000-0000-7000-8000-0000000000aa';
const PROJECT = '0190a000-0000-7000-8000-0000000000bb';

async function settle(fixture: ComponentFixture<unknown>): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve));
  TestBed.tick();
  fixture.detectChanges();
}

const project: ProjectView = {
  id: PROJECT,
  name: 'Customer Portal',
  description: null,
  criticality: 'normal',
  exposure: 'internal',
  archived: false,
  componentCount: 1,
  createdAt: '2026-11-19T08:00:00Z',
  changedAt: '2026-11-19T08:00:00Z',
};

const manual: ComponentView = {
  id: 'c1',
  productKey: 'postgresql',
  name: 'PostgreSQL',
  vendor: 'PostgreSQL Global Development Group',
  inCatalog: true,
  kind: 'database',
  version: '18.1',
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
  versionPrecision: 'exact',
  noticeThreshold: 'only_important',
};

describe('SetupWizard', () => {
  async function render(step: string | null) {
    const user = signal<Me>({
      id: 'me',
      email: 'me@example.org',
      displayName: 'Me',
      isInstanceAdmin: false,
      twoFactorEnabled: true,
      organizations: [
        { id: ORG, slug: 'heron', name: 'Heron Works', role: 'owner', mfaRequired: false },
      ],
    });
    const params = convertToParamMap({ slug: 'heron' });
    TestBed.configureTestingModule({
      imports: [SetupWizard, TranslocoTestingModule.forRoot({ langs: { en: {} } })],
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
    const fixture = TestBed.createComponent(SetupWizard);
    fixture.componentRef.setInput('mode', 'organization');
    if (step) fixture.componentRef.setInput('step', step);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    await settle(fixture);
    return {
      fixture,
      navigate,
      http: TestBed.inject(HttpTestingController),
      element: fixture.nativeElement as HTMLElement,
    };
  }

  it('after the stack, creates the first project and records the step', async () => {
    const { fixture, navigate, http, element } = await render(null);
    http
      .expectOne(`/api/v1/orgs/${ORG}/setup`)
      .flush({ stepsDone: ['stack'], firstProjectId: null, completedAt: null });
    await settle(fixture);

    const name = element.querySelector<HTMLInputElement>('ardea-project-step input')!;
    name.value = 'Customer Portal';
    name.dispatchEvent(new Event('input'));
    element.querySelector<HTMLButtonElement>('ardea-project-step button[type=submit]')!.click();
    await settle(fixture);

    const create = http.expectOne(`/api/v1/orgs/${ORG}/projects`);
    expect(create.request.body).toMatchObject({ name: 'Customer Portal', description: null });
    create.flush(project);
    await settle(fixture);

    const record = http.expectOne(
      (r) => r.url === `/api/v1/orgs/${ORG}/setup` && r.method === 'PATCH',
    );
    expect(record.request.body).toEqual({ stepDone: 'project', firstProjectId: PROJECT });
    record.flush({ stepsDone: ['project'], firstProjectId: PROJECT, completedAt: null });
    await settle(fixture);
    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: { step: 'repository' },
      queryParamsHandling: 'merge',
    });
  });

  it('continues where it stopped, saves the technologies as a whole and finishes', async () => {
    const { fixture, navigate, http, element } = await render(null);
    http.expectOne(`/api/v1/orgs/${ORG}/setup`).flush({
      stepsDone: ['stack', 'project', 'repository', 'operations'],
      firstProjectId: PROJECT,
      completedAt: null,
    });
    await settle(fixture);
    http
      .expectOne(`/api/v1/orgs/${ORG}/projects/${PROJECT}`)
      .flush({ project, components: [manual] });
    http.expectOne(`/api/v1/orgs/${ORG}/projects/${PROJECT}/repositories`).flush([]);
    http
      .expectOne((r) => r.url === `/api/v1/orgs/${ORG}/projects/${PROJECT}/suggestions`)
      .flush([]);
    await settle(fixture);

    expect(element.querySelector('ardea-technologies-step')).not.toBeNull();
    expect(element.querySelector('ardea-draft-list')?.textContent).toContain('PostgreSQL');

    const finish = [
      ...element.querySelectorAll<HTMLButtonElement>('ardea-technologies-step button'),
    ].at(-1)!;
    finish.click();
    await settle(fixture);
    const put = http.expectOne((r) => r.method === 'PUT');
    expect(put.request.body.components).toEqual([
      expect.objectContaining({ id: 'c1', productKey: 'postgresql', version: '18.1' }),
    ]);
    put.flush({ created: 0, updated: 0, removed: 0, unchanged: 1, components: [manual] });
    await settle(fixture);
    const technologies = http.expectOne((r) => r.method === 'PATCH');
    expect(technologies.request.body).toEqual({ stepDone: 'technologies', firstProjectId: null });
    technologies.flush({ stepsDone: [], firstProjectId: PROJECT, completedAt: null });
    await settle(fixture);
    http
      .expectOne(`/api/v1/orgs/${ORG}/setup`)
      .flush({ stepsDone: [], firstProjectId: PROJECT, completedAt: null });
    http
      .expectOne(`/api/v1/orgs/${ORG}/projects/${PROJECT}`)
      .flush({ project, components: [manual] });
    await settle(fixture);
    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: { step: 'coverage' },
      queryParamsHandling: 'merge',
    });

    fixture.componentRef.setInput('step', 'coverage');
    await settle(fixture);
    http
      .expectOne((r) => r.url === `/api/v1/orgs/${ORG}/profile/coverage`)
      .flush({ components: 1, fullyCheckable: 1, hints: [] });
    await settle(fixture);
    expect(element.querySelector('ardea-coverage-card')).not.toBeNull();
    [...element.querySelectorAll<HTMLButtonElement>('.coverage .actions button')].at(-1)!.click();
    await settle(fixture);
    http
      .expectOne((r) => r.method === 'PATCH')
      .flush({ stepsDone: [], firstProjectId: PROJECT, completedAt: null });
    await settle(fixture);
    http
      .expectOne(`/api/v1/orgs/${ORG}/setup`)
      .flush({ stepsDone: [], firstProjectId: PROJECT, completedAt: null });
    http
      .expectOne(`/api/v1/orgs/${ORG}/setup/complete`)
      .flush({ stepsDone: [], firstProjectId: PROJECT, completedAt: '2026-11-19T09:00:00Z' });
    await settle(fixture);
    expect(navigate).toHaveBeenCalledWith(['/o', 'heron', 'projects', PROJECT]);
  });

  const stackEntry: ComponentView = {
    ...manual,
    id: 's1',
    version: null,
    versionPrecision: 'none',
  };

  it('starts with the stack, which needs one product before Continue records the step', async () => {
    const { fixture, navigate, http, element } = await render(null);
    http
      .expectOne(`/api/v1/orgs/${ORG}/setup`)
      .flush({ stepsDone: [], firstProjectId: null, completedAt: null });
    await settle(fixture);
    expect(element.querySelector('ardea-stack-step')).not.toBeNull();
    expect(element.querySelector('ardea-project-step')).toBeNull();
    http.expectOne(`/api/v1/orgs/${ORG}/stack`).flush({ projectId: 'stack', components: [] });
    http.expectOne(`/api/v1/orgs/${ORG}/stack/suggestions`).flush([]);
    await settle(fixture);

    element.querySelector<HTMLButtonElement>('ardea-stack-step .actions button')!.click();
    await settle(fixture);
    http.expectNone((r) => r.method === 'PATCH');
    expect(element.querySelector('ardea-stack-step ui-notice')).not.toBeNull();
    expect(document.activeElement?.getAttribute('role')).toBe('combobox');
    expect(navigate).not.toHaveBeenCalled();
  });

  it('records the stack step once it names a product, and goes on to the optional project', async () => {
    const { fixture, navigate, http, element } = await render(null);
    http
      .expectOne(`/api/v1/orgs/${ORG}/setup`)
      .flush({ stepsDone: [], firstProjectId: null, completedAt: null });
    await settle(fixture);
    http
      .expectOne(`/api/v1/orgs/${ORG}/stack`)
      .flush({ projectId: 'stack', components: [stackEntry] });
    http.expectOne(`/api/v1/orgs/${ORG}/stack/suggestions`).flush([]);
    await settle(fixture);

    element.querySelector<HTMLButtonElement>('ardea-stack-step .actions button')!.click();
    await settle(fixture);
    const record = http.expectOne((r) => r.method === 'PATCH');
    expect(record.request.body).toEqual({ stepDone: 'stack', firstProjectId: null });
    record.flush({ stepsDone: ['stack'], firstProjectId: null, completedAt: null });
    await settle(fixture);
    http
      .expectOne(`/api/v1/orgs/${ORG}/setup`)
      .flush({ stepsDone: ['stack'], firstProjectId: null, completedAt: null });
    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: { step: 'project' },
      queryParamsHandling: 'merge',
    });
  });

  it('finishes without a project: the stack is enough to start', async () => {
    const { fixture, navigate, http, element } = await render('project');
    http
      .expectOne(`/api/v1/orgs/${ORG}/setup`)
      .flush({ stepsDone: ['stack'], firstProjectId: null, completedAt: null });
    await settle(fixture);
    const forward = element.querySelectorAll<HTMLButtonElement>(
      'ardea-project-step .forward button',
    );
    expect(forward.length).toBe(2);
    forward[0].click();
    await settle(fixture);
    http.expectNone(`/api/v1/orgs/${ORG}/projects`);
    http
      .expectOne(`/api/v1/orgs/${ORG}/setup/complete`)
      .flush({ stepsDone: ['stack'], firstProjectId: null, completedAt: '2026-11-19T09:00:00Z' });
    await settle(fixture);
    expect(navigate).toHaveBeenCalledWith(['/o', 'heron', 'overview']);
  });

  it('offers no skip before the stack step is done', async () => {
    const { fixture, http, element } = await render('project');
    http
      .expectOne(`/api/v1/orgs/${ORG}/setup`)
      .flush({ stepsDone: [], firstProjectId: null, completedAt: null });
    await settle(fixture);
    expect(element.querySelectorAll('ardea-project-step .forward button').length).toBe(1);
  });

  it('sends an organization that started before the stack step on to the first step not done', async () => {
    const { fixture, navigate, http, element } = await render(null);
    http.expectOne(`/api/v1/orgs/${ORG}/setup`).flush({
      stepsDone: ['project', 'repository', 'operations'],
      firstProjectId: PROJECT,
      completedAt: null,
    });
    await settle(fixture);
    http
      .expectOne(`/api/v1/orgs/${ORG}/projects/${PROJECT}`)
      .flush({ project, components: [manual] });
    http.expectOne(`/api/v1/orgs/${ORG}/projects/${PROJECT}/repositories`).flush([]);
    http
      .expectOne((r) => r.url === `/api/v1/orgs/${ORG}/projects/${PROJECT}/suggestions`)
      .flush([]);
    await settle(fixture);
    expect(element.querySelector('ardea-stack-step')).not.toBeNull();
    expect(element.querySelectorAll('.stepper li.is-done').length).toBe(4);
    http
      .expectOne(`/api/v1/orgs/${ORG}/stack`)
      .flush({ projectId: 'stack', components: [stackEntry] });
    http.expectOne(`/api/v1/orgs/${ORG}/stack/suggestions`).flush([]);
    await settle(fixture);

    element.querySelector<HTMLButtonElement>('ardea-stack-step .actions button')!.click();
    await settle(fixture);
    http
      .expectOne((r) => r.method === 'PATCH')
      .flush({
        stepsDone: ['stack', 'project', 'repository', 'operations'],
        firstProjectId: PROJECT,
        completedAt: null,
      });
    await settle(fixture);
    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: { step: 'technologies' },
      queryParamsHandling: 'merge',
    });
  });
});
