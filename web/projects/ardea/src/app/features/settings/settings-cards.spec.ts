import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Type } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import type { ConnectionView, SessionOrganization } from '../../core/api/client';
import { AccessesCard } from './accesses-card';
import { AiPolicyCard } from './ai-policy-card';
import { AuditCard } from './audit-card';

const ORG = '0190a000-0000-7000-8000-0000000000aa';
const organization: SessionOrganization = {
  id: ORG,
  slug: 'heron',
  name: 'Heron',
  role: 'admin',
  mfaRequired: false,
};

async function settle(fixture: ComponentFixture<unknown>): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve));
  TestBed.tick();
  fixture.detectChanges();
}

function setUp<T>(type: Type<T>): { fixture: ComponentFixture<T>; http: HttpTestingController } {
  TestBed.configureTestingModule({
    imports: [type, TranslocoTestingModule.forRoot({ langs: { en: {} } })],
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
  });
  const fixture = TestBed.createComponent(type);
  fixture.componentRef.setInput('organization', organization);
  return { fixture, http: TestBed.inject(HttpTestingController) };
}

const connection: ConnectionView = {
  id: 'k1',
  name: 'Example on GitHub',
  hostId: null,
  hostName: 'github.com',
  tokenKind: 'classic',
  tokenHint: 'a91F',
  tokenExpiresAt: '2026-11-25T00:00:00Z',
  expiresSoon: true,
  warnings: ['A classic token; prefer a fine-grained, read-only token for selected repositories.'],
  login: 'ardea-reader',
  lastOkAt: null,
  lastError: null,
  createdAt: '2026-11-19T08:00:00Z',
};

describe('AccessesCard', () => {
  it('shows expiry and warnings, and puts a refused token on its field', async () => {
    const { fixture, http } = setUp(AccessesCard);
    await settle(fixture);
    http.expectOne(`/api/v1/orgs/${ORG}/repository-connections`).flush([connection]);
    http.expectOne('/api/v1/github-hosts').flush([]);
    await settle(fixture);
    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('en.settings.accesses.expiresSoon');
    expect(element.textContent).toContain('A classic token');

    const [name, token] = [...element.querySelectorAll<HTMLInputElement>('form input')];
    name.value = 'Second';
    name.dispatchEvent(new Event('input'));
    token.value = 'github_pat_wrong';
    token.dispatchEvent(new Event('input'));
    element.querySelector<HTMLButtonElement>('form button[type=submit]')!.click();
    await settle(fixture);
    const create = http.expectOne((r) => r.method === 'POST');
    expect(create.request.body).toEqual({
      name: 'Second',
      token: 'github_pat_wrong',
      hostId: null,
    });
    create.flush(
      {
        status: 400,
        code: 'invalid_token',
        errors: { token: ['GitHub does not accept this token.'] },
      },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle(fixture);
    expect(element.textContent).toContain('en.settings.accesses.errors.token');
    expect(token.getAttribute('aria-invalid')).toBe('true');
  });
});

describe('AuditCard', () => {
  it('adds older pages below the ones already shown', async () => {
    const { fixture, http } = setUp(AuditCard);
    const event = (id: string, action: string) => ({
      id,
      at: '2026-11-19T08:00:00Z',
      action,
      subjectType: null,
      subjectId: null,
      data: null,
      origin: 'ui',
      actorKind: 'member',
      actorName: 'Ada',
    });
    await settle(fixture);
    http
      .expectOne((r) => r.url === `/api/v1/orgs/${ORG}/audit`)
      .flush({ items: [event('e2', 'project.created')], nextCursor: 'next' });
    await settle(fixture);
    const element = fixture.nativeElement as HTMLElement;
    element.querySelector<HTMLButtonElement>('button')!.click();
    await settle(fixture);
    const older = http.expectOne((r) => r.url === `/api/v1/orgs/${ORG}/audit`);
    expect(older.request.params.get('cursor')).toBe('next');
    older.flush({ items: [event('e1', 'org.created')], nextCursor: null });
    await settle(fixture);
    expect(element.querySelectorAll('ol li')).toHaveLength(2);
    expect(element.querySelector('button')).toBeNull();
  });
});

describe('AiPolicyCard', () => {
  it('saves the chosen AI data policy and nothing else', async () => {
    const { fixture, http } = setUp(AiPolicyCard);
    await settle(fixture);
    const view = {
      id: ORG,
      slug: 'heron',
      name: 'Heron',
      locale: 'en',
      timeZone: 'Europe/Zurich',
      requireMfa: false,
      aiPolicy: 'local_only',
      role: 'admin',
      createdAt: '2026-11-19T08:00:00Z',
    };
    http.expectOne(`/api/v1/orgs/${ORG}`).flush(view);
    await settle(fixture);
    const element = fixture.nativeElement as HTMLElement;
    const radios = [...element.querySelectorAll<HTMLInputElement>('input[type=radio]')];
    expect(radios.map((r) => r.value)).toEqual(['none', 'local_only', 'cloud_allowed']);
    expect(radios[1].checked).toBe(true);
    const save = element.querySelector<HTMLButtonElement>('ui-card button')!;
    expect(save.disabled).toBe(true);

    radios[0].click();
    await settle(fixture);
    save.click();
    await settle(fixture);
    const patch = http.expectOne((r) => r.method === 'PATCH');
    expect(patch.request.body).toEqual({
      name: null,
      locale: null,
      timeZone: null,
      requireMfa: null,
      aiPolicy: 'none',
    });
    patch.flush({ ...view, aiPolicy: 'none' });
    await settle(fixture);
    expect(element.textContent).toContain('en.settings.ai.saved');
  });
});
