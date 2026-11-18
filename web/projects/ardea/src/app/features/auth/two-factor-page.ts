import { Component, ElementRef, inject, input, signal } from '@angular/core';
import { form, FormField, FormRoot, required, validate } from '@angular/forms/signals';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { UiButton, UiField, UiInput, UiNotice } from 'ui';
import { firstValueFrom } from 'rxjs';
import { AuthenticationService } from '../../core/api/client';
import { problemMessageKey } from '../../core/api/problem';
import { safeReturnUrl } from '../../core/session/access';
import { Session } from '../../core/session/session';
import { fieldErrorKey, normalizeRecoveryCode, normalizeTotp } from './auth-forms';
import { focusFirstInvalid } from './focus-invalid';

/**
 * The second step of signing in: a 6-digit code from the authenticator app, or one of the
 * recovery codes. Pasting is allowed and spaces are ignored (WCAG 2.2, accessible authentication).
 */
@Component({
  selector: 'ardea-two-factor-page',
  imports: [FormField, FormRoot, RouterLink, TranslocoPipe, UiButton, UiField, UiInput, UiNotice],
  template: `
    <h1>{{ 'auth.twoFactor.title' | transloco }}</h1>
    <p class="lead">
      {{ (useRecovery() ? 'auth.twoFactor.leadRecovery' : 'auth.twoFactor.lead') | transloco }}
    </p>

    <form class="auth-form" [formRoot]="verify">
      @if (problem()) {
        <ui-notice tone="error">{{ problem() | transloco }}</ui-notice>
      }

      @if (useRecovery()) {
        <ui-field
          [label]="'auth.twoFactor.recoveryLabel' | transloco"
          [error]="fieldErrorKey(verify.code, recoveryErrors) | transloco"
        >
          <input
            uiInput
            mono
            type="text"
            autocomplete="off"
            autocapitalize="characters"
            spellcheck="false"
            [formField]="verify.code"
          />
        </ui-field>
      } @else {
        <ui-field
          [label]="'auth.twoFactor.codeLabel' | transloco"
          [error]="fieldErrorKey(verify.code, codeErrors) | transloco"
        >
          <input
            uiInput
            mono
            type="text"
            inputmode="numeric"
            autocomplete="one-time-code"
            spellcheck="false"
            [formField]="verify.code"
          />
        </ui-field>
      }

      <button uiButton variant="primary" block type="submit" [loading]="verify().submitting()">
        {{ 'auth.twoFactor.submit' | transloco }}
      </button>
      <button uiButton variant="ghost" block type="button" (click)="switchMode()">
        {{ (useRecovery() ? 'auth.twoFactor.useApp' : 'auth.twoFactor.useRecovery') | transloco }}
      </button>
    </form>

    <p class="auth-foot">
      <a class="link" routerLink="/sign-in">{{ 'auth.backToSignIn' | transloco }}</a>
    </p>
  `,
})
export class TwoFactorPage {
  readonly returnUrl = input<string | null>(null);

  private readonly auth = inject(AuthenticationService);
  private readonly session = inject(Session);
  private readonly router = inject(Router);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  protected readonly fieldErrorKey = fieldErrorKey;
  protected readonly codeErrors = {
    required: 'auth.twoFactor.errors.codeRequired',
    totp: 'auth.twoFactor.errors.codeFormat',
  };
  protected readonly recoveryErrors = { required: 'auth.twoFactor.errors.recoveryRequired' };
  protected readonly useRecovery = signal(false);
  protected readonly problem = signal<string | null>(null);

  private readonly model = signal({ code: '' });
  protected readonly verify = form(
    this.model,
    (p) => {
      required(p.code);
      validate(p.code, ({ value }) =>
        !this.useRecovery() && value() && !/^\d{6}$/.test(normalizeTotp(value()))
          ? { kind: 'totp' }
          : undefined,
      );
    },
    {
      submission: {
        action: async () => {
          this.problem.set(null);
          const recovery = this.useRecovery();
          const raw = this.model().code;
          try {
            await firstValueFrom(
              this.auth.loginTwoFactor({
                code: recovery ? normalizeRecoveryCode(raw) : normalizeTotp(raw),
                isRecoveryCode: recovery,
              }),
            );
            await this.session.refresh();
            await this.router.navigateByUrl(safeReturnUrl(this.returnUrl()) ?? '/');
          } catch (error) {
            const key = problemMessageKey(error, {
              codes: { two_factor_expired: 'auth.twoFactor.errors.expired' },
              statuses: {
                400: 'auth.twoFactor.errors.invalid',
                401: 'auth.twoFactor.errors.invalid',
              },
              fallback: 'auth.twoFactor.errors.invalid',
            });
            if (key === 'auth.twoFactor.errors.expired') {
              await this.router.navigate(['/sign-in'], {
                queryParams: { notice: 'twoFactorExpired' },
              });
              return;
            }
            this.problem.set(
              recovery && key === 'auth.twoFactor.errors.invalid'
                ? 'auth.twoFactor.errors.invalidRecovery'
                : key,
            );
          }
        },
        onInvalid: () => focusFirstInvalid(this.host),
      },
    },
  );

  protected switchMode(): void {
    this.useRecovery.update((v) => !v);
    this.model.set({ code: '' });
    this.verify().reset();
    this.problem.set(null);
  }
}
