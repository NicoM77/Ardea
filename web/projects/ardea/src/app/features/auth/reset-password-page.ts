import { Component, ElementRef, inject, input, signal } from '@angular/core';
import { form, FormField, FormRoot } from '@angular/forms/signals';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { UiButton, UiField, UiInput, UiNotice, UiPasswordToggle } from 'ui';
import { firstValueFrom } from 'rxjs';
import { AuthenticationService } from '../../core/api/client';
import { problemMessageKey } from '../../core/api/problem';
import {
  fieldErrorKey,
  NEW_PASSWORD_ERRORS,
  newPasswordRules,
  weakPasswordError,
} from './auth-forms';
import { focusFirstInvalid } from './focus-invalid';

@Component({
  selector: 'ardea-reset-password-page',
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
    <h1>{{ 'auth.reset.title' | transloco }}</h1>

    @if (!email() || !token()) {
      <ui-notice tone="error" [heading]="'auth.reset.missing.heading' | transloco">
        {{ 'auth.reset.missing.text' | transloco }}
      </ui-notice>
      <p class="auth-foot">
        <a class="link" routerLink="/forgot-password">{{ 'auth.reset.requestNew' | transloco }}</a>
      </p>
    } @else {
      <p class="lead">{{ 'auth.reset.lead' | transloco: { email: email() } }}</p>
      <form class="auth-form" [formRoot]="reset">
        @if (problem()) {
          <ui-notice tone="error">
            {{ problem() | transloco }}
            @if (linkExpired()) {
              <a class="link" routerLink="/forgot-password">{{
                'auth.reset.requestNew' | transloco
              }}</a>
            }
          </ui-notice>
        }
        <input
          class="sr-only"
          type="email"
          autocomplete="username"
          tabindex="-1"
          aria-hidden="true"
          readonly
          [value]="email()"
        />
        <ui-field
          [label]="'auth.fields.newPassword.label' | transloco"
          [hint]="'auth.fields.newPassword.hint' | transloco"
          [error]="fieldErrorKey(reset.newPassword, passwordErrors) | transloco"
        >
          <input
            uiInput
            #pw
            type="password"
            autocomplete="new-password"
            [formField]="reset.newPassword"
          />
          <ui-password-toggle [control]="pw" />
        </ui-field>
        <button uiButton variant="primary" block type="submit" [loading]="reset().submitting()">
          {{ 'auth.reset.submit' | transloco }}
        </button>
      </form>
      <p class="auth-foot">
        <a class="link" routerLink="/sign-in">{{ 'auth.backToSignIn' | transloco }}</a>
      </p>
    }
  `,
})
export class ResetPasswordPage {
  readonly email = input<string | null>(null);
  readonly token = input<string | null>(null);

  private readonly auth = inject(AuthenticationService);
  private readonly router = inject(Router);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  protected readonly fieldErrorKey = fieldErrorKey;
  protected readonly passwordErrors = NEW_PASSWORD_ERRORS;
  protected readonly problem = signal<string | null>(null);
  protected readonly linkExpired = signal(false);

  private readonly model = signal({ newPassword: '' });
  protected readonly reset = form(this.model, (p) => newPasswordRules(p.newPassword), {
    submission: {
      action: async () => {
        this.problem.set(null);
        this.linkExpired.set(false);
        try {
          await firstValueFrom(
            this.auth.resetPassword({
              email: this.email() ?? '',
              token: this.token() ?? '',
              newPassword: this.model().newPassword,
            }),
          );
          await this.router.navigate(['/sign-in'], { queryParams: { notice: 'passwordChanged' } });
        } catch (error) {
          const weak = weakPasswordError(error, this.reset.newPassword);
          if (weak) {
            focusFirstInvalid(this.host);
            return weak;
          }
          const key = problemMessageKey(error, {
            codes: { invalid_token: 'auth.reset.errors.linkInvalid' },
            statuses: { 400: 'auth.reset.errors.linkInvalid' },
            fallback: 'auth.reset.errors.rejected',
          });
          this.linkExpired.set(key === 'auth.reset.errors.linkInvalid');
          this.problem.set(key);
        }
        return undefined;
      },
      onInvalid: () => focusFirstInvalid(this.host),
    },
  });
}
