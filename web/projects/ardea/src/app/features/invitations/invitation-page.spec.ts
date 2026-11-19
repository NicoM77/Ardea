import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { computed, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { Session, type Me } from '../../core/session/session';
import { InvitationPage } from './invitation-page';

async function settle(fixture: ComponentFixture<unknown>): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve));
  TestBed.tick();
  fixture.detectChanges();
}

const PREVIEW = {
  organizationName: 'Heron Works',
  email: 'jane@example.org',
  role: 'member',
  invitedBy: 'Ada Admin',
  expiresAt: '2026-11-25T08:00:00Z',
};

describe('InvitationPage', () => {
  async function render(email: string | null, preview: object | null = PREVIEW) {
    const user = signal<Me | null>(
      email
        ? {
            id: 'u1',
            email,
            displayName: 'Jane',
            isInstanceAdmin: false,
            twoFactorEnabled: false,
            organizations: [],
          }
        : null,
    );
    const refresh = vi.fn(async () => {
      user.set(null);
    });
    TestBed.configureTestingModule({
      imports: [InvitationPage, TranslocoTestingModule.forRoot({ langs: { en: {} } })],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: Session,
          useValue: {
            user: computed(() => user()),
            session: { isLoading: () => false },
            refresh,
          },
        },
      ],
    });
    const fixture = TestBed.createComponent(InvitationPage);
    fixture.componentRef.setInput('token', 't0k3n');
    await settle(fixture);
    const http = TestBed.inject(HttpTestingController);
    const request = http.expectOne('/api/v1/invitations/t0k3n');
    if (preview) request.flush(preview);
    else
      request.flush(
        { status: 404, code: 'invalid_invitation' },
        { status: 404, statusText: 'Not Found' },
      );
    await settle(fixture);
    return { fixture, http, refresh, element: fixture.nativeElement as HTMLElement };
  }

  it('offers a stranger to register with the invited address, or to sign in', async () => {
    const { element } = await render(null);
    expect(element.textContent).toContain('jane@example.org');
    const links = [...element.querySelectorAll<HTMLAnchorElement>('a[uiButton]')].map((a) =>
      a.getAttribute('href'),
    );
    expect(links).toEqual([
      '/register?invitation=t0k3n&email=jane@example.org',
      '/sign-in?returnUrl=%2Finvitations%2Ft0k3n',
    ]);
  });

  it('accepts for the invited address and opens the organization', async () => {
    const { fixture, http, element } = await render('Jane@Example.org');
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const accept = element.querySelector<HTMLButtonElement>('button')!;
    expect(accept.textContent).toContain('en.invitation.accept');
    accept.click();
    await settle(fixture);
    const post = http.expectOne('/api/v1/invitations/t0k3n/accept');
    expect(post.request.method).toBe('POST');
    post.flush({ organizationId: 'o1', slug: 'heron', name: 'Heron Works' });
    await settle(fixture);
    await settle(fixture);
    expect(navigate).toHaveBeenCalledWith(['/o', 'heron', 'overview']);
  });

  it('explains another account and signs it out', async () => {
    const { fixture, http, refresh, element } = await render('someone@example.org');
    expect(element.textContent).toContain('en.invitation.otherAccount.text');
    element.querySelector<HTMLButtonElement>('button')!.click();
    await settle(fixture);
    http.expectOne('/api/v1/auth/logout').flush(null);
    await settle(fixture);
    await settle(fixture);
    expect(refresh).toHaveBeenCalled();
    expect(element.textContent).toContain('en.invitation.signedOut');
  });

  it('says when the invitation no longer works', async () => {
    const { element } = await render(null, null);
    expect(element.textContent).toContain('en.invitation.invalid.heading');
    expect(element.textContent).toContain('en.invitation.errors.invalid');
  });
});
