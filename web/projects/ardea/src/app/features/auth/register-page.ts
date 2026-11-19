import { Component, computed, ElementRef, inject, input, OnInit, signal } from '@angular/core';
import { form, FormField, FormRoot } from '@angular/forms/signals';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { UiButton, UiField, UiInput, UiNotice, UiPasswordToggle } from 'ui';
import { firstValueFrom } from 'rxjs';
import { AuthenticationService, OrganizationsService } from '../../core/api/client';
import { problemMessageKey } from '../../core/api/problem';
import { Session } from '../../core/session/session';
import {
  displayNameRules,
  EMAIL_ERRORS,
  emailRules,
  fieldErrorKey,
  NAME_ERRORS,
  NEW_PASSWORD_ERRORS,
  newPasswordRules,
  weakPasswordError,
} from './auth-forms';
import { focusFirstInvalid } from './focus-invalid';

/**
 * Creating an account, usually from an invitation (`/register?invitation=...&email=...`). With
 * an invitation the address comes filled in, and the new account accepts it right away.
 */
@Component({
  selector: 'ardea-register-page',
  imports: [
    FormField,
    FormRoot,
    RouterLink,
    TranslocoPipe,
    UiButton,
    UiField,
    UiInput,
    UiNotice,
    UiPasswordToggle,
  ],
  template: `
    <h1>{{ 'auth.register.title' | transloco }}</h1>

    <!-- Without an invitation the instance may still be open; the API answers if it is not. -->
    <p class="lead">
      {{ (invitation() ? 'auth.register.lead' : 'auth.register.leadOpen') | transloco }}
    </p>

    <form class="auth-form" [formRoot]="register">
      @if (problem()) {
        <ui-notice tone="error">{{ problem() | transloco }}</ui-notice>
      }

      <ui-field
        [label]="'auth.fields.email.label' | transloco"
        [hint]="invitation() ? ('auth.register.emailHint' | transloco) : null"
        [error]="fieldErrorKey(register.email, emailErrors) | transloco"
      >
        <input
          uiInput
          type="email"
          autocomplete="email"
          spellcheck="false"
          [formField]="register.email"
        />
      </ui-field>

      <ui-field
        [label]="'auth.fields.displayName.label' | transloco"
        [hint]="'auth.fields.displayName.hint' | transloco"
        [error]="fieldErrorKey(register.displayName, nameErrors) | transloco"
      >
        <input uiInput type="text" autocomplete="name" [formField]="register.displayName" />
      </ui-field>

      <ui-field
        [label]="'auth.fields.password.label' | transloco"
        [hint]="'auth.fields.newPassword.hint' | transloco"
        [error]="fieldErrorKey(register.password, passwordErrors) | transloco"
      >
        <input
          uiInput
          #pw
          type="password"
          autocomplete="new-password"
          [formField]="register.password"
        />
        <ui-password-toggle [control]="pw" />
      </ui-field>

      <button uiButton variant="primary" block type="submit" [loading]="register().submitting()">
        {{ 'auth.register.submit' | transloco }}
      </button>
    </form>

    <p class="auth-foot">
      <span>{{ 'auth.register.haveAccount' | transloco }}</span>
      <a class="link" routerLink="/sign-in" [queryParams]="signInParams()">{{
        'auth.signIn.title' | transloco
      }}</a>
    </p>
  `,
})
export class RegisterPage implements OnInit {
  /** The token from the invitation link. */
  readonly invitation = input<string | null>(null);
  /** The invited address, handed over by the invitation page. */
  readonly email = input<string | null>(null);

  private readonly auth = inject(AuthenticationService);
  private readonly organizations = inject(OrganizationsService);
  private readonly session = inject(Session);
  private readonly router = inject(Router);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  protected readonly fieldErrorKey = fieldErrorKey;
  protected readonly emailErrors = EMAIL_ERRORS;
  protected readonly nameErrors = NAME_ERRORS;
  protected readonly passwordErrors = NEW_PASSWORD_ERRORS;
  protected readonly problem = signal<string | null>(null);
  /** Signing in instead comes back to the invitation. */
  protected readonly signInParams = computed(() => {
    const invitation = this.invitation();
    return invitation ? { returnUrl: `/invitations/${invitation}` } : {};
  });

  private readonly model = signal({ email: '', displayName: '', password: '' });
  protected readonly register = form(
    this.model,
    (p) => {
      emailRules(p.email);
      displayNameRules(p.displayName);
      newPasswordRules(p.password);
    },
    {
      submission: {
        action: async () => {
          this.problem.set(null);
          try {
            await firstValueFrom(
              this.auth.register({ ...this.model(), invitationToken: this.invitation() }),
            );
            // The API may sign the new account in right away; if it does not, sign-in says why.
            await this.session.refresh();
            const invitation = this.invitation();
            if (this.session.state() !== 'signedIn') {
              // An address that has an account already: sign in with it, then accept.
              await this.router.navigate(['/sign-in'], {
                queryParams: invitation
                  ? { notice: 'registered', returnUrl: `/invitations/${invitation}` }
                  : { notice: 'registered' },
              });
            } else if (invitation) {
              await this.join(invitation);
            } else {
              await this.router.navigateByUrl('/');
            }
          } catch (error) {
            const weak = weakPasswordError(error, this.register.password);
            if (weak) {
              focusFirstInvalid(this.host);
              return weak;
            }
            // A taken address answers 204 like a new one (master plan 9.3), so it is no error here.
            this.problem.set(
              problemMessageKey(error, {
                codes: {
                  invalid_invitation: this.invitation()
                    ? 'auth.register.errors.invitation'
                    : 'auth.register.errors.invitationRequired',
                  registration_closed: 'auth.register.errors.closed',
                  signed_in: 'auth.register.errors.signedIn',
                },
                fallback: 'auth.register.errors.rejected',
              }),
            );
          }
          return undefined;
        },
        onInvalid: () => focusFirstInvalid(this.host),
      },
    },
  );

  ngOnInit(): void {
    const email = this.email();
    if (email) this.model.update((model) => ({ ...model, email }));
  }

  /**
   * Accepts the invitation the account was created for. If that fails, the invitation page
   * explains why and offers what is left to do.
   */
  private async join(token: string): Promise<void> {
    try {
      const joined = await firstValueFrom(this.organizations.acceptInvitation(token));
      await this.session.refresh();
      await this.router.navigate(['/o', joined.slug, 'overview']);
    } catch {
      await this.router.navigate(['/invitations', token]);
    }
  }
}
