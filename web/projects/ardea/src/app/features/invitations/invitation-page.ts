import { Component, computed, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiLoader, UiNotice } from 'ui';
import {
  AuthenticationService,
  OrganizationsService,
  previewInvitationResource,
} from '../../core/api/client';
import { problemMessageKey, problemOf } from '../../core/api/problem';
import { Session } from '../../core/session/session';
import { ROLE_HINTS, sameEmail } from '../organizations/org-format';

const PROBLEMS = {
  invalid_invitation: 'invitation.errors.invalid',
  invitation_email_mismatch: 'invitation.errors.mismatch',
  signed_in: 'invitation.errors.signedIn',
};

@Component({
  selector: 'ardea-invitation-page',
  imports: [RouterLink, TranslocoPipe, UiButton, UiLoader, UiNotice],
  template: `
    @if (preview.hasValue() && preview.value(); as offer) {
      <h1>{{ 'invitation.heading' | transloco: { organization: offer.organizationName } }}</h1>
      <p class="lead">
        {{
          'invitation.lead'
            | transloco
              : {
                  by: offer.invitedBy,
                  organization: offer.organizationName,
                  role: offer.role,
                }
        }}
      </p>

      <dl class="facts">
        <div>
          <dt>{{ 'invitation.for' | transloco }}</dt>
          <dd>{{ offer.email }}</dd>
        </div>
        <div>
          <dt>{{ 'invitation.until' | transloco }}</dt>
          <dd>
            <time [attr.datetime]="offer.expiresAt">{{ when(offer.expiresAt) }}</time>
          </dd>
        </div>
        @if (roleHints[offer.role]; as hint) {
          <div>
            <dt>{{ 'invitation.role' | transloco }}</dt>
            <dd>{{ hint | transloco }}</dd>
          </div>
        }
      </dl>

      <div class="auth-form" [attr.aria-busy]="busy()">
        @if (problem()) {
          <ui-notice tone="error">{{ problem() | transloco }}</ui-notice>
        }
        @if (sessionLoading()) {
          <ui-loader [label]="'invitation.loading' | transloco" />
        } @else if (user(); as me) {
          @if (matches()) {
            <button
              uiButton
              variant="primary"
              block
              type="button"
              [loading]="busy()"
              (click)="accept()"
            >
              {{ 'invitation.accept' | transloco }}
            </button>
          } @else {
            <ui-notice [heading]="'invitation.otherAccount.heading' | transloco">
              {{
                'invitation.otherAccount.text'
                  | transloco: { current: me.email, invited: offer.email }
              }}
            </ui-notice>
            <button
              uiButton
              variant="primary"
              block
              type="button"
              [loading]="busy()"
              (click)="signOut()"
            >
              {{ 'invitation.otherAccount.signOut' | transloco }}
            </button>
          }
        } @else {
          <p>{{ 'invitation.signedOut' | transloco: { email: offer.email } }}</p>
          <div class="auth-actions">
            <a
              uiButton
              variant="primary"
              block
              routerLink="/register"
              [queryParams]="{ invitation: token(), email: offer.email }"
              >{{ 'invitation.register' | transloco }}</a
            >
            <a uiButton block routerLink="/sign-in" [queryParams]="{ returnUrl: here() }">{{
              'invitation.signIn' | transloco
            }}</a>
          </div>
        }
      </div>
    } @else if (preview.error()) {
      <h1>{{ 'invitation.title' | transloco }}</h1>
      <ui-notice tone="error" [heading]="'invitation.invalid.heading' | transloco">
        {{ previewProblem() | transloco }}
      </ui-notice>
      <p class="auth-foot">
        <a class="link" routerLink="/">{{ 'invitation.home' | transloco }}</a>
      </p>
    } @else {
      <h1>{{ 'invitation.title' | transloco }}</h1>
      <div aria-busy="true">
        <ui-loader [label]="'invitation.loading' | transloco" />
      </div>
    }
  `,
  styles: `
    .facts {
      display: grid;
      gap: var(--space-sm);
      margin-bottom: var(--gap);
      padding: var(--space-md) 0;
      border-block: 1px solid var(--rule);
    }
    .facts div {
      display: grid;
      grid-template-columns: 7.5rem minmax(0, 1fr);
      gap: var(--space-md);
    }
    dt {
      color: var(--muted);
    }
    dd {
      overflow-wrap: anywhere;
    }
    @media (max-width: 640px) {
      .facts div {
        grid-template-columns: minmax(0, 1fr);
        gap: 0;
      }
    }
  `,
})
export class InvitationPage {
  readonly token = input.required<string>();

  private readonly organizations = inject(OrganizationsService);
  private readonly auth = inject(AuthenticationService);
  private readonly session = inject(Session);
  private readonly router = inject(Router);
  private readonly transloco = inject(TranslocoService);

  protected readonly roleHints = ROLE_HINTS;
  protected readonly preview = previewInvitationResource(this.token);
  protected readonly user = this.session.user;
  protected readonly sessionLoading = computed(() => this.session.session.isLoading());
  protected readonly matches = computed(() =>
    sameEmail(this.user()?.email, this.preview.hasValue() ? this.preview.value()?.email : null),
  );
  protected readonly here = computed(() => `/invitations/${this.token()}`);
  protected readonly busy = signal(false);
  protected readonly problem = signal<string | null>(null);
  protected readonly previewProblem = computed(() =>
    problemMessageKey(this.preview.error(), {
      codes: PROBLEMS,
      statuses: { 404: 'invitation.errors.invalid' },
      fallback: 'invitation.errors.invalid',
    }),
  );

  protected async accept(): Promise<void> {
    await this.step(async () => {
      const joined = await firstValueFrom(this.organizations.acceptInvitation(this.token()));
      await this.session.refresh();
      await this.router.navigate(['/o', joined.slug, 'overview']);
    });
  }

  protected async signOut(): Promise<void> {
    await this.step(async () => {
      try {
        await firstValueFrom(this.auth.logout());
      } finally {
        await this.session.refresh();
      }
    });
  }

  protected when(value: string): string {
    return new Intl.DateTimeFormat(this.transloco.getActiveLang(), { dateStyle: 'long' }).format(
      new Date(value),
    );
  }

  private async step(work: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.problem.set(null);
    try {
      await work();
    } catch (error) {
      if (problemOf(error)?.code === 'invalid_invitation') this.preview.reload();
      this.problem.set(
        problemMessageKey(error, { codes: PROBLEMS, fallback: 'invitation.errors.failed' }),
      );
    } finally {
      this.busy.set(false);
    }
  }
}
