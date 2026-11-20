import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { computed, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { of } from 'rxjs';
import type { InvitationView, MemberView } from '../../core/api/client';
import { Session, type Me } from '../../core/session/session';
import { TeamPage } from './team-page';

const ORG_ID = '0190a000-0000-7000-8000-0000000000aa';

const member = (userId: string, role: string): MemberView => ({
  userId,
  displayName: `Person ${userId}`,
  email: `${userId}@example.org`,
  role,
  joinedAt: '2026-11-18T08:00:00Z',
});

const invitation = (extra: Partial<InvitationView> = {}): InvitationView => ({
  id: 'inv-1',
  email: 'new@example.org',
  role: 'member',
  expiresAt: '2026-11-25T08:00:00Z',
  expired: false,
  invitedBy: 'Person me',
  createdAt: '2026-11-18T08:00:00Z',
  link: null,
  ...extra,
});

async function settle(fixture: ComponentFixture<unknown>): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve));
  TestBed.tick();
  fixture.detectChanges();
}

describe('TeamPage', () => {
  async function render(role: string) {
    const user = signal<Me>({
      id: 'me',
      email: 'me@example.org',
      displayName: 'Person me',
      isInstanceAdmin: false,
      twoFactorEnabled: true,
      organizations: [{ id: ORG_ID, slug: 'heron', name: 'Heron Works', role, mfaRequired: false }],
    });
    const refresh = vi.fn(async () => undefined);
    TestBed.configureTestingModule({
      imports: [TeamPage, TranslocoTestingModule.forRoot({ langs: { en: {} } })],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: Session, useValue: { user: computed(() => user()), refresh } },
        {
          provide: ActivatedRoute,
          useValue: { parent: { paramMap: of(convertToParamMap({ slug: 'heron' })) } },
        },
      ],
    });
    const fixture = TestBed.createComponent(TeamPage);
    await settle(fixture);
    const http = TestBed.inject(HttpTestingController);
    http
      .expectOne(`/api/v1/orgs/${ORG_ID}/members`)
      .flush([member('me', role), member('ada', 'admin'), member('vic', 'viewer')]);
    await settle(fixture);
    return { fixture, http, refresh, element: fixture.nativeElement as HTMLElement };
  }

  const button = (element: HTMLElement, label: string) =>
    [...element.querySelectorAll<HTMLButtonElement>('button')].find(
      (b) => b.textContent?.trim() === label,
    );

  it('lets the owner hand ownership on after confirming', async () => {
    const { fixture, http, refresh, element } = await render('owner');
    http.expectOne(`/api/v1/orgs/${ORG_ID}/invitations`).flush([invitation()]);
    await settle(fixture);

    expect(element.querySelectorAll('select[aria-label]').length).toBe(2);
    expect(element.textContent).toContain('new@example.org');

    element
      .querySelector<HTMLButtonElement>('button[aria-label="en.team.members.transferLabel"]')!
      .click();
    await settle(fixture);
    expect(element.querySelector('.confirm')?.textContent).toContain(
      'en.team.members.confirmTransfer',
    );
    element.querySelector<HTMLButtonElement>('.confirm-button')!.click();
    await settle(fixture);

    const patch = http.expectOne(`/api/v1/orgs/${ORG_ID}/members/ada`);
    expect(patch.request.method).toBe('PATCH');
    expect(patch.request.body).toEqual({ role: 'owner' });
    patch.flush(member('ada', 'owner'));
    await settle(fixture);
    expect(refresh).toHaveBeenCalled();
    http.expectOne(`/api/v1/orgs/${ORG_ID}/members`).flush([member('ada', 'owner')]);
    await settle(fixture);
    expect(element.textContent).toContain('en.team.members.transferred');
  });

  it('shows a viewer the directory, a way to leave, and nothing to manage', async () => {
    const { http, element } = await render('viewer');
    http.expectNone(`/api/v1/orgs/${ORG_ID}/invitations`);
    expect(element.querySelector('select')).toBeNull();
    expect(element.querySelector('ardea-invite-card')).toBeNull();
    expect(button(element, 'en.team.members.leave')).toBeDefined();
    expect(button(element, 'en.team.members.remove')).toBeUndefined();
  });

  it('shows the link of an invitation once when the instance sends no e-mail', async () => {
    const { fixture, http, element } = await render('admin');
    http.expectOne(`/api/v1/orgs/${ORG_ID}/invitations`).flush([]);
    await settle(fixture);

    const email = element.querySelector<HTMLInputElement>('ardea-invite-card input[type=email]')!;
    email.value = 'new@example.org';
    email.dispatchEvent(new Event('input'));
    element.querySelector<HTMLButtonElement>('ardea-invite-card button[type=submit]')!.click();
    await settle(fixture);

    const post = http.expectOne(`/api/v1/orgs/${ORG_ID}/invitations`);
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({ email: 'new@example.org', role: 'member' });
    post.flush(invitation({ link: 'https://ardea.example.org/invitations/t0k3n' }));
    await settle(fixture);
    http.expectOne(`/api/v1/orgs/${ORG_ID}/invitations`).flush([invitation()]);
    await settle(fixture);

    const link = element.querySelector<HTMLInputElement>('#invitation-link')!;
    expect(link.value).toBe('https://ardea.example.org/invitations/t0k3n');
    expect(element.textContent).toContain('en.team.invite.linkText');
    expect(email.value).toBe('');

    button(element, 'en.team.invite.done')!.click();
    await settle(fixture);
    expect(element.querySelector('#invitation-link')).toBeNull();
  });

  it('puts an existing member on the e-mail field', async () => {
    const { fixture, http, element } = await render('admin');
    http.expectOne(`/api/v1/orgs/${ORG_ID}/invitations`).flush([]);
    await settle(fixture);

    const email = element.querySelector<HTMLInputElement>('ardea-invite-card input[type=email]')!;
    email.value = 'ada@example.org';
    email.dispatchEvent(new Event('input'));
    element.querySelector<HTMLButtonElement>('ardea-invite-card button[type=submit]')!.click();
    await settle(fixture);
    http
      .expectOne(`/api/v1/orgs/${ORG_ID}/invitations`)
      .flush({ status: 409, code: 'already_member' }, { status: 409, statusText: 'Conflict' });
    await settle(fixture);
    expect(element.querySelector('ardea-invite-card')?.textContent).toContain(
      'en.team.errors.alreadyMember',
    );
  });
});
