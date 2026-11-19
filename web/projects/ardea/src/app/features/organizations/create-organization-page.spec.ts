import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { Session } from '../../core/session/session';
import { CreateOrganizationPage } from './create-organization-page';

async function settle(fixture: ComponentFixture<unknown>): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve));
  TestBed.tick();
  fixture.detectChanges();
}

describe('CreateOrganizationPage', () => {
  async function render() {
    const refresh = vi.fn(async () => undefined);
    TestBed.configureTestingModule({
      imports: [
        CreateOrganizationPage,
        TranslocoTestingModule.forRoot({
          langs: { en: { organizations: { create: { slugHint: 'At {{path}}' } } } },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
        }),
      ],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: Session, useValue: { refresh } },
      ],
    });
    const fixture = TestBed.createComponent(CreateOrganizationPage);
    await settle(fixture);
    const element = fixture.nativeElement as HTMLElement;
    const [name, slug] = [...element.querySelectorAll<HTMLInputElement>('input')];
    const type = async (input: HTMLInputElement, value: string) => {
      input.value = value;
      input.dispatchEvent(new Event('input'));
      await settle(fixture);
    };
    const submit = async () => {
      element.querySelector<HTMLButtonElement>('button[type=submit]')!.click();
      await settle(fixture);
    };
    return {
      fixture,
      element,
      name,
      slug,
      type,
      submit,
      refresh,
      http: TestBed.inject(HttpTestingController),
    };
  }

  it('previews the address from the name and lets the API derive it', async () => {
    const { fixture, element, name, slug, type, submit, refresh, http } = await render();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    await type(name, 'Heron Works');
    expect(slug.placeholder).toBe('heron-works');
    expect(element.textContent).toContain('At /o/heron-works');

    await submit();
    const post = http.expectOne('/api/v1/orgs');
    expect(post.request.body).toEqual({ name: 'Heron Works', slug: null });
    post.flush({ id: 'o1', slug: 'heron-works', name: 'Heron Works', role: 'owner' });
    await settle(fixture);
    await settle(fixture);
    expect(refresh).toHaveBeenCalled();
    expect(navigate).toHaveBeenCalledWith(['/o', 'heron-works', 'team']);
  });

  it('asks for another address when the API says it is taken', async () => {
    const { fixture, element, name, slug, type, submit, http } = await render();
    await type(name, 'Heron Works');
    await type(slug, 'heron');
    expect(element.textContent).toContain('At /o/heron');

    await submit();
    http
      .expectOne('/api/v1/orgs')
      .flush({ status: 409, code: 'slug_taken' }, { status: 409, statusText: 'Conflict' });
    await settle(fixture);
    expect(element.textContent).toContain('organizations.create.errors.slugTaken');
    expect(slug.getAttribute('aria-invalid')).toBe('true');
  });

  it('checks the address before sending it', async () => {
    const { element, name, slug, type, submit, http } = await render();
    await type(name, 'Heron');
    await type(slug, 'Not A Slug');
    await submit();
    http.expectNone('/api/v1/orgs');
    expect(element.textContent).toContain('organizations.create.errors.slugFormat');
  });
});
