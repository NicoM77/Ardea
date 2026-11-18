import { Component, ElementRef, inject, input, signal } from '@angular/core';
import { form, FormField, FormRoot } from '@angular/forms/signals';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { UiButton, UiField, UiInput, UiNotice, UiPasswordToggle } from 'ui';
import { AuthApi } from '../../core/api/auth-api';
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
} from './auth-forms';
import { focusFirstInvalid } from './focus-invalid';

/** Creating an account from an invitation link (`/register?invitation=...`). */
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

    @if (!invitation()) {
      <ui-notice tone="error" [heading]="'auth.register.missing.heading' | transloco">
        {{ 'auth.register.missing.text' | transloco }}
      </ui-notice>
    } @else {
      <p class="lead">{{ 'auth.register.lead' | transloco }}</p>

      <form class="auth-form" [formRoot]="register">
        @if (problem()) {
          <ui-notice tone="error">{{ problem() | transloco }}</ui-notice>
        }

        <ui-field
          [label]="'auth.fields.email.label' | transloco"
          [hint]="'auth.register.emailHint' | transloco"
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
    }

    <p class="auth-foot">
      <span>{{ 'auth.register.haveAccount' | transloco }}</span>
      <a class="link" routerLink="/sign-in">{{ 'auth.signIn.title' | transloco }}</a>
    </p>
  `,
})
export class RegisterPage {
  /** The token from the invitation link. */
  readonly invitation = input<string | null>(null);

  private readonly auth = inject(AuthApi);
  private readonly session = inject(Session);
  private readonly router = inject(Router);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  protected readonly fieldErrorKey = fieldErrorKey;
  protected readonly emailErrors = EMAIL_ERRORS;
  protected readonly nameErrors = NAME_ERRORS;
  protected readonly passwordErrors = NEW_PASSWORD_ERRORS;
  protected readonly problem = signal<string | null>(null);

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
            await this.auth.register({ ...this.model(), invitationToken: this.invitation() ?? '' });
            // The API may sign the new account in right away; if it does not, sign-in says why.
            await this.session.refresh();
            if (this.session.state() === 'signedIn') {
              await this.router.navigateByUrl('/');
            } else {
              await this.router.navigate(['/sign-in'], { queryParams: { notice: 'registered' } });
            }
          } catch (error) {
            this.problem.set(
              problemMessageKey(error, {
                codes: {
                  invalid_invitation: 'auth.register.errors.invitation',
                  email_taken: 'auth.register.errors.emailTaken',
                },
                statuses: {
                  404: 'auth.register.errors.invitation',
                  409: 'auth.register.errors.emailTaken',
                  410: 'auth.register.errors.invitation',
                },
                fallback: 'auth.register.errors.rejected',
              }),
            );
          }
        },
        onInvalid: () => focusFirstInvalid(this.host),
      },
    },
  );
}
