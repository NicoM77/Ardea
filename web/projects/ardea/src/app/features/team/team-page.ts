import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { map } from 'rxjs';
import { UiPageHeader } from 'ui';
import { listInvitationsResource, listMembersResource } from '../../core/api/client';
import { problemOf } from '../../core/api/problem';
import { Session } from '../../core/session/session';
import { MfaRequired } from '../organizations/mfa-required';
import { canManage, organizationBySlug } from '../organizations/org-format';
import { InvitationsCard } from './invitations-card';
import { InviteCard } from './invite-card';
import { MembersCard } from './members-card';

@Component({
  selector: 'ardea-team-page',
  imports: [TranslocoPipe, UiPageHeader, MfaRequired, InvitationsCard, InviteCard, MembersCard],
  template: `
    @if (organization(); as org) {
      @if (mfaRefused()) {
        <ardea-mfa-required [organization]="org.name" [returnUrl]="here()" />
      } @else {
        <ui-page-header
          [heading]="'nav.team' | transloco"
          [sub]="'team.lead' | transloco: { name: org.name }"
        />
        <div class="grid">
          <div class="main">
            <ardea-members-card [organization]="org" [members]="members" />
            @if (manage()) {
              <ardea-invitations-card [organization]="org" [invitations]="invitations" />
            }
          </div>
          @if (manage()) {
            <div class="side">
              <ardea-invite-card [organization]="org" (invited)="invitations.reload()" />
            </div>
          }
        </div>
      }
    }
  `,
  styles: `
    :host {
      display: block;
    }
    .grid {
      display: grid;
      grid-template-columns: repeat(12, minmax(0, 1fr));
      gap: var(--gap);
      align-items: start;
    }
    .main {
      grid-column: span 8;
    }
    .side {
      grid-column: span 4;
    }
    .main,
    .side {
      display: grid;
      gap: var(--gap);
      min-width: 0;
    }
    .main:only-child {
      grid-column: span 12;
    }
    @media (max-width: 1100px) {
      .main,
      .side {
        grid-column: span 12;
      }
    }
  `,
})
export class TeamPage {
  private readonly session = inject(Session);
  private readonly router = inject(Router);

  private readonly slug = toSignal(
    (inject(ActivatedRoute).parent ?? inject(ActivatedRoute)).paramMap.pipe(
      map((params) => params.get('slug')),
    ),
    { initialValue: null },
  );

  protected readonly organization = computed(() =>
    organizationBySlug(this.session.user(), this.slug()),
  );
  protected readonly manage = computed(() => canManage(this.organization()?.role));
  private readonly orgId = computed(() => this.organization()?.id);
  private readonly adminOrgId = computed(() => (this.manage() ? this.orgId() : undefined));

  protected readonly members = listMembersResource(this.orgId);
  protected readonly invitations = listInvitationsResource(this.adminOrgId);

  protected readonly mfaRefused = computed(
    () => problemOf(this.members.error())?.code === 'mfa_required',
  );
  protected readonly here = computed(() => {
    const slug = this.slug();
    return slug ? `/o/${slug}/team` : this.router.url;
  });
}
