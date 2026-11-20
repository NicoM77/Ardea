import { Component, ElementRef, inject, signal } from '@angular/core';
import { form, FormField, FormRoot } from '@angular/forms/signals';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { UiButton, UiField, UiInput, UiNotice } from 'ui';
import { firstValueFrom } from 'rxjs';
import { AuthenticationService } from '../../core/api/client';
import { problemMessageKey } from '../../core/api/problem';
import { EMAIL_ERRORS, emailRules, fieldErrorKey } from './auth-forms';
import { focusFirstInvalid } from './focus-invalid';

@Component({
  selector: 'ardea-forgot-password-page',
  imports: [FormField, FormRoot, RouterLink, TranslocoPipe, UiButton, UiField, UiInput, UiNotice],
  template: `
    <h1>{{ 'auth.forgot.title' | transloco }}</h1>

    @if (sentTo(); as email) {
      <ui-notice tone="success" [heading]="'auth.forgot.sent.heading' | transloco">
        {{ 'auth.forgot.sent.text' | transloco: { email } }}
      </ui-notice>
    } @else {
      <p class="lead">{{ 'auth.forgot.lead' | transloco }}</p>
      <form class="auth-form" [formRoot]="forgot">
        @if (problem()) {
          <ui-notice tone="error">{{ problem() | transloco }}</ui-notice>
        }
        <ui-field
          [label]="'auth.fields.email.label' | transloco"
          [error]="fieldErrorKey(forgot.email, emailErrors) | transloco"
        >
          <input
            uiInput
            type="email"
            autocomplete="email"
            spellcheck="false"
            [formField]="forgot.email"
          />
        </ui-field>
        <button uiButton variant="primary" block type="submit" [loading]="forgot().submitting()">
          {{ 'auth.forgot.submit' | transloco }}
        </button>
      </form>
    }

    <p class="auth-foot">
      <a class="link" routerLink="/sign-in">{{ 'auth.backToSignIn' | transloco }}</a>
    </p>
  `,
})
export class ForgotPasswordPage {
  private readonly auth = inject(AuthenticationService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  protected readonly fieldErrorKey = fieldErrorKey;
  protected readonly emailErrors = EMAIL_ERRORS;
  protected readonly problem = signal<string | null>(null);
  protected readonly sentTo = signal<string | null>(null);

  private readonly model = signal({ email: '' });
  protected readonly forgot = form(this.model, (p) => emailRules(p.email), {
    submission: {
      action: async () => {
        this.problem.set(null);
        const email = this.model().email.trim();
        try {
          await firstValueFrom(this.auth.forgotPassword({ email }));
          this.sentTo.set(email);
        } catch (error) {
          this.problem.set(problemMessageKey(error, { fallback: 'auth.forgot.errors.failed' }));
        }
      },
      onInvalid: () => focusFirstInvalid(this.host),
    },
  });
}
