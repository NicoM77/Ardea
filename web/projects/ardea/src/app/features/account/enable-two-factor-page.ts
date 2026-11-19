import { Component, computed, ElementRef, inject, input, signal } from '@angular/core';
import { form, FormField, FormRoot, required, validate } from '@angular/forms/signals';
import { DomSanitizer, SafeUrl } from '@angular/platform-browser';
import { Router } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { LucideShieldCheck } from '@lucide/angular';
import {
  UiButton,
  UiCard,
  UiCopyButton,
  UiField,
  UiInput,
  UiNotice,
  UiPageHeader,
  UiPasswordToggle,
  UiQrCode,
} from 'ui';
import { firstValueFrom } from 'rxjs';
import { AccountService, type MfaSetupResponse } from '../../core/api/client';
import { problemMessageKey } from '../../core/api/problem';
import { safeReturnUrl } from '../../core/session/access';
import { Session } from '../../core/session/session';
import { fieldErrorKey, formatSharedKey, normalizeTotp } from '../auth/auth-forms';
import { focusFirstInvalid } from '../auth/focus-invalid';
import { RecoveryCodes } from './recovery-codes';

type Step = 'intro' | 'scan' | 'codes';

/**
 * Turning on two-factor authentication in three steps: start, scan the code (or type the key)
 * and confirm with a code from the app, then save the recovery codes, which are shown only once.
 */
@Component({
  selector: 'ardea-enable-two-factor-page',
  imports: [
    FormField,
    FormRoot,
    TranslocoPipe,
    LucideShieldCheck,
    UiButton,
    UiCard,
    UiCopyButton,
    UiField,
    UiInput,
    UiNotice,
    UiPageHeader,
    UiPasswordToggle,
    UiQrCode,
    RecoveryCodes,
  ],
  template: `
    <ui-page-header
      [heading]="'account.twoFactor.title' | transloco"
      [sub]="'account.twoFactor.sub' | transloco"
    />

    <div class="column">
      @switch (step()) {
        @case ('intro') {
          @if (alreadyOn()) {
            <ui-card>
              <div class="state">
                <svg lucideShieldCheck aria-hidden="true"></svg>
                <div>
                  <p class="state-title">{{ 'account.twoFactor.on.heading' | transloco }}</p>
                  <p class="muted">{{ 'account.twoFactor.on.text' | transloco }}</p>
                </div>
              </div>
            </ui-card>
          } @else {
            <ui-card [heading]="'account.twoFactor.intro.heading' | transloco">
              <p class="muted">{{ 'account.twoFactor.intro.text' | transloco }}</p>
              <form class="confirm gap-top" [formRoot]="intro">
                @if (problem()) {
                  <ui-notice tone="error">{{ problem() | transloco }}</ui-notice>
                }
                <ui-field
                  [label]="'account.twoFactor.intro.passwordLabel' | transloco"
                  [hint]="'account.twoFactor.intro.passwordHint' | transloco"
                  [error]="fieldErrorKey(intro.password, passwordErrors) | transloco"
                >
                  <input
                    uiInput
                    #pw
                    type="password"
                    autocomplete="current-password"
                    [formField]="intro.password"
                  />
                  <ui-password-toggle [control]="pw" />
                </ui-field>
                <div>
                  <button uiButton variant="primary" type="submit" [loading]="intro().submitting()">
                    {{ 'account.twoFactor.intro.start' | transloco }}
                  </button>
                </div>
              </form>
            </ui-card>
          }
        }

        @case ('scan') {
          @if (setup(); as s) {
            <ui-card [heading]="'account.twoFactor.scan.heading' | transloco">
              <span uiCardMeta class="steps">{{
                'account.twoFactor.step' | transloco: { current: 1, total: 2 }
              }}</span>
              <div class="scan">
                <ui-qr-code
                  [value]="s.authenticatorUri"
                  [label]="'account.twoFactor.scan.qrLabel' | transloco"
                />
                <div class="scan-text">
                  <p>{{ 'account.twoFactor.scan.text' | transloco }}</p>
                  <div class="key-block">
                    <p class="muted">{{ 'account.twoFactor.scan.keyLabel' | transloco }}</p>
                    <p class="key id">{{ sharedKey() }}</p>
                    <div class="key-tools">
                      <ui-copy-button
                        [text]="s.sharedKey"
                        [label]="'account.twoFactor.scan.copyKey' | transloco"
                      />
                      @if (authenticatorLink(); as link) {
                        <a uiButton variant="ghost" [href]="link">{{
                          'account.twoFactor.scan.openApp' | transloco
                        }}</a>
                      }
                    </div>
                  </div>
                </div>
              </div>
            </ui-card>

            <ui-card [heading]="'account.twoFactor.confirm.heading' | transloco">
              <span uiCardMeta class="steps">{{
                'account.twoFactor.step' | transloco: { current: 2, total: 2 }
              }}</span>
              <form class="confirm" [formRoot]="confirm">
                @if (problem()) {
                  <ui-notice tone="error">{{ problem() | transloco }}</ui-notice>
                }
                <ui-field
                  [label]="'account.twoFactor.confirm.codeLabel' | transloco"
                  [error]="fieldErrorKey(confirm.code, codeErrors) | transloco"
                >
                  <input
                    uiInput
                    mono
                    type="text"
                    inputmode="numeric"
                    autocomplete="one-time-code"
                    spellcheck="false"
                    [formField]="confirm.code"
                  />
                </ui-field>
                <div>
                  <button
                    uiButton
                    variant="primary"
                    type="submit"
                    [loading]="confirm().submitting()"
                  >
                    {{ 'account.twoFactor.confirm.submit' | transloco }}
                  </button>
                </div>
              </form>
            </ui-card>
          }
        }

        @case ('codes') {
          <ui-notice tone="success">{{ 'account.twoFactor.codes.enabled' | transloco }}</ui-notice>
          <ui-card [heading]="'account.twoFactor.codes.heading' | transloco">
            <span uiCardMeta>{{
              'account.twoFactor.codes.count' | transloco: { count: recoveryCodes().length }
            }}</span>
            <p class="muted">{{ 'account.twoFactor.codes.text' | transloco }}</p>
            <ardea-recovery-codes class="gap-top" [codes]="recoveryCodes()" />
            <div class="done">
              <button uiButton variant="primary" type="button" (click)="finish()">
                {{ 'account.twoFactor.codes.done' | transloco }}
              </button>
            </div>
          </ui-card>
        }
      }
    </div>
  `,
  styles: `
    .column {
      display: grid;
      gap: var(--gap);
      max-width: 640px;
    }
    .muted {
      color: var(--muted);
    }
    .gap-top {
      margin-top: var(--space-lg);
    }
    .steps {
      color: var(--faint);
    }
    .state {
      display: flex;
      gap: var(--space-md);
    }
    .state svg {
      width: 20px;
      height: 20px;
      color: var(--positive);
    }
    .state-title {
      font-weight: 500;
    }
    .scan {
      display: grid;
      grid-template-columns: 176px minmax(0, 1fr);
      gap: var(--gap);
      align-items: start;
    }
    .scan-text {
      display: grid;
      gap: var(--space-lg);
    }
    .key-block {
      display: grid;
      gap: var(--space-xs);
    }
    .key {
      font-size: var(--fs-section);
      line-height: var(--lh-section);
      letter-spacing: 0.06em;
      white-space: normal;
      overflow-wrap: anywhere;
      user-select: all;
    }
    .key-tools {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-sm);
      margin-top: var(--space-sm);
    }
    .confirm {
      display: grid;
      gap: var(--space-lg);
      max-width: 320px;
    }
    .done {
      margin-top: var(--gap);
      padding-top: var(--space-lg);
      border-top: 1px solid var(--rule);
    }
    @media (max-width: 640px) {
      .scan {
        grid-template-columns: minmax(0, 1fr);
        justify-items: start;
      }
    }
  `,
})
export class EnableTwoFactorPage {
  /** Where to go once it is on, such as an organization that requires it. */
  readonly returnUrl = input<string | null>(null);

  private readonly account = inject(AccountService);
  private readonly session = inject(Session);
  private readonly router = inject(Router);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  protected readonly fieldErrorKey = fieldErrorKey;
  protected readonly codeErrors = {
    required: 'account.twoFactor.errors.codeRequired',
    totp: 'account.twoFactor.errors.codeFormat',
  };

  protected readonly passwordErrors = { required: 'account.twoFactor.errors.passwordRequired' };

  protected readonly step = signal<Step>('intro');
  protected readonly problem = signal<string | null>(null);
  protected readonly setup = signal<MfaSetupResponse | null>(null);
  protected readonly recoveryCodes = signal<string[]>([]);
  protected readonly alreadyOn = computed(() => this.session.user()?.twoFactorEnabled === true);
  protected readonly sharedKey = computed(() => formatSharedKey(this.setup()?.sharedKey ?? ''));

  /** On a phone the URI opens the authenticator app directly; only `otpauth://` URIs are linked. */
  protected readonly authenticatorLink = computed<SafeUrl | null>(() => {
    const uri = this.setup()?.authenticatorUri ?? '';
    return uri.startsWith('otpauth://totp/') ? this.sanitizer.bypassSecurityTrustUrl(uri) : null;
  });

  /** Setup starts only with the password re-entered: a stolen session alone cannot bind the account to another phone. */
  private readonly introModel = signal({ password: '' });
  protected readonly intro = form(
    this.introModel,
    (p) => {
      required(p.password);
    },
    {
      submission: {
        action: async () => {
          this.problem.set(null);
          try {
            this.setup.set(
              await firstValueFrom(
                this.account.startMfaSetup({ password: this.introModel().password }),
              ),
            );
            this.introModel.set({ password: '' });
            this.step.set('scan');
          } catch (error) {
            this.problem.set(
              problemMessageKey(error, {
                codes: { invalid_password: 'account.twoFactor.errors.wrongPassword' },
                fallback: 'account.twoFactor.errors.failed',
              }),
            );
          }
        },
        onInvalid: () => focusFirstInvalid(this.host),
      },
    },
  );

  private readonly model = signal({ code: '' });
  protected readonly confirm = form(
    this.model,
    (p) => {
      required(p.code);
      validate(p.code, ({ value }) =>
        value() && !/^\d{6}$/.test(normalizeTotp(value())) ? { kind: 'totp' } : undefined,
      );
    },
    {
      submission: {
        action: async () => {
          this.problem.set(null);
          try {
            const result = await firstValueFrom(
              this.account.enableMfa({
                code: normalizeTotp(this.model().code),
              }),
            );
            this.recoveryCodes.set(result.recoveryCodes);
            this.step.set('codes');
          } catch (error) {
            this.problem.set(
              problemMessageKey(error, {
                codes: { invalid_code: 'account.twoFactor.errors.invalid' },
                statuses: { 400: 'account.twoFactor.errors.invalid' },
                fallback: 'account.twoFactor.errors.failed',
              }),
            );
          }
        },
        onInvalid: () => focusFirstInvalid(this.host),
      },
    },
  );

  protected async finish(): Promise<void> {
    await this.session.refresh();
    await this.router.navigateByUrl(safeReturnUrl(this.returnUrl()) ?? '/');
  }
}
