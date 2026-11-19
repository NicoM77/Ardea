import { HttpResourceRef } from '@angular/common/http';
import { Component, computed, inject, input, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiCard, UiLoader, UiNotice, UiPill } from 'ui';
import {
  OrganizationsService,
  type InvitationView,
  type SessionOrganization,
} from '../../core/api/client';
import { problemMessageKey, problemOf } from '../../core/api/problem';
import { ROLE_LABELS } from '../organizations/org-format';
import { TEAM_PROBLEMS } from './team-problems';

/** Open invitations with their role, who sent them and until when they work; each can be revoked. */
@Component({
  selector: 'ardea-invitations-card',
  imports: [TranslocoPipe, UiButton, UiCard, UiLoader, UiNotice, UiPill],
  template: `
    <ui-card
      [heading]="'team.invitations.title' | transloco"
      [attr.aria-busy]="invitations().isLoading()"
    >
      <div class="live" role="status">
        @if (revoked()) {
          <ui-notice tone="success">{{
            'team.invitations.revoked' | transloco: { email: revoked() }
          }}</ui-notice>
        }
      </div>
      @if (problem()) {
        <ui-notice class="gap" tone="error">{{ problem() | transloco }}</ui-notice>
      }

      @if (list(); as rows) {
        @if (rows.length) {
          <ul class="invitations" [attr.aria-label]="'team.invitations.title' | transloco">
            @for (invitation of rows; track invitation.id) {
              <li>
                <div class="what">
                  <span class="email">{{ invitation.email }}</span>
                  <span class="sub">
                    {{
                      'team.invitations.meta'
                        | transloco
                          : {
                              role: (roleLabels[invitation.role] ?? invitation.role | transloco),
                              by: invitation.invitedBy,
                            }
                    }}
                  </span>
                </div>
                <div class="when">
                  @if (invitation.expired) {
                    <ui-pill>{{ 'team.invitations.expired' | transloco }}</ui-pill>
                  } @else {
                    <span class="sub">
                      {{
                        'team.invitations.until' | transloco: { date: when(invitation.expiresAt) }
                      }}
                    </span>
                  }
                </div>
                <button
                  uiButton
                  variant="ghost"
                  type="button"
                  [loading]="busy() === invitation.id"
                  [attr.aria-label]="
                    'team.invitations.revokeLabel' | transloco: { email: invitation.email }
                  "
                  (click)="revoke(invitation)"
                >
                  {{ 'team.invitations.revoke' | transloco }}
                </button>
              </li>
            }
          </ul>
        } @else {
          <p class="muted">{{ 'team.invitations.none' | transloco }}</p>
        }
      } @else if (invitations().error()) {
        <ui-notice tone="error">{{ 'team.invitations.loadFailed' | transloco }}</ui-notice>
        <div class="gap">
          <button uiButton type="button" (click)="invitations().reload()">
            {{ 'team.tryAgain' | transloco }}
          </button>
        </div>
      } @else {
        <ui-loader [label]="'team.invitations.loading' | transloco" />
      }
    </ui-card>
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }
    .live:empty {
      display: none;
    }
    .live,
    .gap {
      margin-bottom: var(--space-md);
    }
    .invitations {
      display: grid;
    }
    li {
      display: grid;
      grid-template-columns: minmax(0, 1fr) auto auto;
      align-items: center;
      gap: var(--space-sm) var(--space-md);
      min-height: 52px;
      padding: var(--space-sm) 0;
    }
    li + li {
      border-top: 1px solid var(--rule);
    }
    .what {
      display: grid;
      min-width: 0;
    }
    .email {
      font-weight: 500;
      overflow-wrap: anywhere;
    }
    .sub,
    .muted {
      color: var(--muted);
    }
    .sub {
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    @media (max-width: 640px) {
      li {
        grid-template-columns: minmax(0, 1fr) auto;
      }
      .when {
        grid-row: 2;
      }
    }
  `,
})
export class InvitationsCard {
  readonly organization = input.required<SessionOrganization>();
  readonly invitations = input.required<HttpResourceRef<InvitationView[] | undefined>>();

  private readonly api = inject(OrganizationsService);
  private readonly transloco = inject(TranslocoService);

  protected readonly roleLabels = ROLE_LABELS;
  protected readonly busy = signal<string | null>(null);
  protected readonly revoked = signal<string | null>(null);
  protected readonly problem = signal<string | null>(null);
  protected readonly list = computed(() => {
    const invitations = this.invitations();
    return invitations.hasValue() ? (invitations.value() ?? null) : null;
  });

  protected async revoke(invitation: InvitationView): Promise<void> {
    this.busy.set(invitation.id);
    this.problem.set(null);
    this.revoked.set(null);
    try {
      await firstValueFrom(this.api.revokeInvitation(this.organization().id, invitation.id));
      this.revoked.set(invitation.email);
      this.invitations().reload();
    } catch (error) {
      // Gone already (accepted or revoked elsewhere): the list catches up.
      if (problemOf(error)?.code === 'invitation_not_found') this.invitations().reload();
      this.problem.set(
        problemMessageKey(error, { codes: TEAM_PROBLEMS, fallback: 'team.errors.failed' }),
      );
    } finally {
      this.busy.set(null);
    }
  }

  protected when(value: string): string {
    return new Intl.DateTimeFormat(this.transloco.getActiveLang(), { dateStyle: 'medium' }).format(
      new Date(value),
    );
  }
}
