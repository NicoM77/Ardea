import { Component, ElementRef, inject, signal } from '@angular/core';
import { form, FormField, FormRoot, required } from '@angular/forms/signals';
import { Router } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { UiButton, UiField, UiInput, UiNotice, UiPasswordToggle } from 'ui';
import { firstValueFrom } from 'rxjs';
import { SetupService } from '../../core/api/client';
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

/** First start of an instance: the person holding the setup token creates the first account. */
@Component({
  selector: 'ardea-setup-page',
  imports: [
    FormField,
    FormRoot,
    TranslocoPipe,
    UiButton,
    UiField,
    UiInput,
    UiNotice,
    UiPasswordToggle,
  ],
  template: `
    <h1>{{ 'auth.setup.title' | transloco }}</h1>
    <p class="lead">{{ 'auth.setup.lead' | transloco }}</p>

    <form class="auth-form" [formRoot]="setup">
      @if (problem()) {
        <ui-notice tone="error">{{ problem() | transloco }}</ui-notice>
      }

      <ui-field
        [label]="'auth.setup.tokenLabel' | transloco"
        [hint]="'auth.setup.tokenHint' | transloco"
        [error]="fieldErrorKey(setup.setupToken, tokenErrors) | transloco"
      >
        <input
          uiInput
          mono
          type="text"
          autocomplete="off"
          spellcheck="false"
          [formField]="setup.setupToken"
        />
      </ui-field>

      <ui-field
        [label]="'auth.fields.email.label' | transloco"
        [error]="fieldErrorKey(setup.email, emailErrors) | transloco"
      >
        <input
          uiInput
          type="email"
          autocomplete="email"
          spellcheck="false"
          [formField]="setup.email"
        />
      </ui-field>

      <ui-field
        [label]="'auth.fields.displayName.label' | transloco"
        [hint]="'auth.fields.displayName.hint' | transloco"
        [error]="fieldErrorKey(setup.displayName, nameErrors) | transloco"
      >
        <input uiInput type="text" autocomplete="name" [formField]="setup.displayName" />
      </ui-field>

      <ui-field
        [label]="'auth.fields.password.label' | transloco"
        [hint]="'auth.fields.newPassword.hint' | transloco"
        [error]="fieldErrorKey(setup.password, passwordErrors) | transloco"
      >
        <input
          uiInput
          #pw
          type="password"
          autocomplete="new-password"
          [formField]="setup.password"
        />
        <ui-password-toggle [control]="pw" />
      </ui-field>

      <button uiButton variant="primary" block type="submit" [loading]="setup().submitting()">
        {{ 'auth.setup.submit' | transloco }}
      </button>
    </form>
  `,
})
export class SetupPage {
  private readonly setupApi = inject(SetupService);
  private readonly session = inject(Session);
  private readonly router = inject(Router);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  protected readonly fieldErrorKey = fieldErrorKey;
  protected readonly tokenErrors = { required: 'auth.setup.errors.tokenRequired' };
  protected readonly emailErrors = EMAIL_ERRORS;
  protected readonly nameErrors = NAME_ERRORS;
  protected readonly passwordErrors = NEW_PASSWORD_ERRORS;
  protected readonly problem = signal<string | null>(null);

  private readonly model = signal({ setupToken: '', email: '', displayName: '', password: '' });
  protected readonly setup = form(
    this.model,
    (p) => {
      required(p.setupToken);
      emailRules(p.email);
      displayNameRules(p.displayName);
      newPasswordRules(p.password);
    },
    {
      submission: {
        action: async () => {
          this.problem.set(null);
          const value = this.model();
          try {
            await firstValueFrom(
              this.setupApi.completeSetup({ ...value, setupToken: value.setupToken.trim() }),
            );
            await this.session.refresh();
            await this.router.navigateByUrl('/');
          } catch (error) {
            const weak = weakPasswordError(error, this.setup.password);
            if (weak) {
              focusFirstInvalid(this.host);
              return weak;
            }
            const key = problemMessageKey(error, {
              codes: {
                invalid_setup_token: 'auth.setup.errors.tokenInvalid',
                setup_completed: 'auth.setup.errors.completed',
              },
              statuses: {
                401: 'auth.setup.errors.tokenInvalid',
                403: 'auth.setup.errors.tokenInvalid',
                409: 'auth.setup.errors.completed',
              },
              fallback: 'auth.setup.errors.rejected',
            });
            if (key === 'auth.setup.errors.completed') {
              await this.session.refresh();
              await this.router.navigate(['/sign-in']);
              return undefined;
            }
            this.problem.set(key);
          }
          return undefined;
        },
        onInvalid: () => focusFirstInvalid(this.host),
      },
    },
  );
}
