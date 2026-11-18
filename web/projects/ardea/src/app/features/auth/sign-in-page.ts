import { Component, computed, ElementRef, inject, input, signal } from '@angular/core';
import { form, FormField, FormRoot, required } from '@angular/forms/signals';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { UiButton, UiField, UiInput, UiNotice, UiPasswordToggle } from 'ui';
import { firstValueFrom } from 'rxjs';
import { AuthenticationService } from '../../core/api/client';
import { problemMessageKey } from '../../core/api/problem';
import { safeReturnUrl } from '../../core/session/access';
import { Session } from '../../core/session/session';
import { EMAIL_ERRORS, emailRules, fieldErrorKey, PASSWORD_ERRORS } from './auth-forms';
import { focusFirstInvalid } from './focus-invalid';

/** Messages another page hands over when it sends someone here. */
const NOTICES: Record<string, string> = {
  passwordChanged: 'auth.signIn.notices.passwordChanged',
  registered: 'auth.signIn.notices.registered',
  signedOut: 'auth.signIn.notices.signedOut',
  twoFactorExpired: 'auth.signIn.notices.twoFactorExpired',
};

@Component({
  selector: 'ardea-sign-in-page',
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
    <h1>{{ 'auth.signIn.title' | transloco }}</h1>
    <p class="lead">{{ 'auth.signIn.lead' | transloco }}</p>

    <form class="auth-form" [formRoot]="signIn">
      @if (problem()) {
        <ui-notice tone="error">{{ problem() | transloco }}</ui-notice>
      } @else if (noticeKey()) {
        <ui-notice tone="success">{{ noticeKey() | transloco }}</ui-notice>
      }

      <ui-field
        [label]="'auth.fields.email.label' | transloco"
        [error]="fieldErrorKey(signIn.email, emailErrors) | transloco"
      >
        <input
          uiInput
          type="email"
          autocomplete="username"
          spellcheck="false"
          [formField]="signIn.email"
        />
      </ui-field>

      <ui-field
        [label]="'auth.fields.password.label' | transloco"
        [error]="fieldErrorKey(signIn.password, passwordErrors) | transloco"
      >
        <input
          uiInput
          #pw
          type="password"
          autocomplete="current-password"
          [formField]="signIn.password"
        />
        <ui-password-toggle [control]="pw" />
      </ui-field>

      <button uiButton variant="primary" block type="submit" [loading]="signIn().submitting()">
        {{ 'auth.signIn.submit' | transloco }}
      </button>
    </form>

    <p class="auth-foot">
      <a class="link" routerLink="/forgot-password">{{ 'auth.signIn.forgot' | transloco }}</a>
    </p>
  `,
})
export class SignInPage {
  readonly returnUrl = input<string | null>(null);
  readonly notice = input<string | null>(null);

  private readonly auth = inject(AuthenticationService);
  private readonly session = inject(Session);
  private readonly router = inject(Router);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  protected readonly emailErrors = EMAIL_ERRORS;
  protected readonly passwordErrors = PASSWORD_ERRORS;
  protected readonly fieldErrorKey = fieldErrorKey;
  protected readonly problem = signal<string | null>(null);
  protected readonly noticeKey = computed(() => NOTICES[this.notice() ?? ''] ?? null);

  private readonly model = signal({ email: '', password: '' });
  protected readonly signIn = form(
    this.model,
    (p) => {
      emailRules(p.email);
      required(p.password);
    },
    {
      submission: {
        action: async () => {
          this.problem.set(null);
          try {
            const result = await firstValueFrom(this.auth.login(this.model()));
            const returnUrl = safeReturnUrl(this.returnUrl());
            if (result.status === 'twoFactorRequired') {
              await this.router.navigate(['/sign-in/two-factor'], {
                queryParams: returnUrl ? { returnUrl } : {},
              });
              return;
            }
            await this.session.refresh();
            await this.router.navigateByUrl(returnUrl ?? '/');
          } catch (error) {
            this.problem.set(
              problemMessageKey(error, {
                codes: {
                  invalid_credentials: 'auth.signIn.errors.invalid',
                  locked_out: 'auth.signIn.errors.lockedOut',
                },
                statuses: { 400: 'auth.signIn.errors.invalid', 401: 'auth.signIn.errors.invalid' },
                fallback: 'auth.signIn.errors.invalid',
              }),
            );
          }
        },
        onInvalid: () => focusFirstInvalid(this.host),
      },
    },
  );
}
