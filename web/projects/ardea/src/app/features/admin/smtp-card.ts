import {
  Component,
  computed,
  ElementRef,
  inject,
  input,
  linkedSignal,
  output,
  signal,
} from '@angular/core';
import {
  disabled,
  email,
  form,
  FormField,
  FormRoot,
  pattern,
  required,
} from '@angular/forms/signals';
import type { TreeValidationResult } from '@angular/forms/signals';
import { TranslocoPipe } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiCard, UiField, UiInput, UiNotice, UiPasswordToggle } from 'ui';
import {
  AdministrationService,
  type InstanceSettingsResponse,
  type SettingView,
} from '../../core/api/client';
import { problemMessageKey, problemOf } from '../../core/api/problem';
import { fieldErrorKey } from '../auth/auth-forms';
import { focusFirstInvalid } from '../auth/focus-invalid';
import { fieldProblems } from './admin-errors';
import { hasChanges, NO_CHANGES, SMTP_SECURITY, smtpFormOf, smtpPatch } from './settings-patch';

/**
 * SMTP settings with a test e-mail (master plan 11.4, 12.4). Values set by an environment variable
 * are read-only. The password is write-only: the page says whether one is set and offers a field
 * to replace it, never the value.
 */
@Component({
  selector: 'ardea-smtp-card',
  imports: [
    FormField,
    FormRoot,
    TranslocoPipe,
    UiButton,
    UiCard,
    UiField,
    UiInput,
    UiNotice,
    UiPasswordToggle,
  ],
  template: `
    <ui-card [heading]="'admin.smtp.title' | transloco">
      <p class="intro">{{ 'admin.smtp.intro' | transloco }}</p>

      <form class="grid" [formRoot]="smtp">
        @if (saveProblem()) {
          <ui-notice class="wide" tone="error">{{ saveProblem() | transloco }}</ui-notice>
        } @else if (saved()) {
          <ui-notice class="wide" tone="success">{{ 'admin.smtp.saved' | transloco }}</ui-notice>
        }

        <ui-field
          class="wide"
          [label]="'admin.smtp.host' | transloco"
          [hint]="lockHint(settings().smtp.host) | transloco"
          [error]="fieldErrorKey(smtp.host, errors.host) | transloco"
        >
          <input
            uiInput
            type="text"
            autocomplete="off"
            spellcheck="false"
            [formField]="smtp.host"
          />
        </ui-field>

        <ui-field
          [label]="'admin.smtp.port' | transloco"
          [hint]="lockHint(settings().smtp.port) ?? 'admin.smtp.portHint' | transloco"
          [error]="fieldErrorKey(smtp.port, errors.port) | transloco"
        >
          <input
            uiInput
            type="text"
            inputmode="numeric"
            autocomplete="off"
            [formField]="smtp.port"
          />
        </ui-field>

        <ui-field
          [label]="'admin.smtp.security' | transloco"
          [hint]="lockHint(settings().smtp.security) | transloco"
          [error]="fieldErrorKey(smtp.security, errors.security) | transloco"
        >
          <select uiInput [formField]="smtp.security">
            <option value="">{{ 'admin.smtp.securityDefault' | transloco }}</option>
            @for (mode of securityModes; track mode) {
              <option [value]="mode">{{ securityLabels[mode] | transloco }}</option>
            }
          </select>
        </ui-field>

        <ui-field
          [label]="'admin.smtp.user' | transloco"
          [hint]="lockHint(settings().smtp.user) | transloco"
          optional
        >
          <input
            uiInput
            type="text"
            autocomplete="off"
            spellcheck="false"
            [formField]="smtp.user"
          />
        </ui-field>

        <ui-field
          [label]="'admin.smtp.password' | transloco"
          [hint]="passwordHint() | transloco"
          optional
        >
          <input
            uiInput
            #pw
            type="password"
            autocomplete="new-password"
            [placeholder]="
              (settings().smtp.password.isSet ? 'admin.smtp.passwordReplace' : '') | transloco
            "
            [formField]="smtp.newPassword"
          />
          <ui-password-toggle [control]="pw" />
        </ui-field>

        <ui-field
          class="wide"
          [label]="'admin.smtp.from' | transloco"
          [hint]="lockHint(settings().smtp.from) ?? 'admin.smtp.fromHint' | transloco"
          [error]="fieldErrorKey(smtp.from, errors.from) | transloco"
        >
          <input
            uiInput
            type="email"
            autocomplete="off"
            spellcheck="false"
            [formField]="smtp.from"
          />
        </ui-field>

        <div class="wide actions">
          <button uiButton variant="primary" type="submit" [loading]="smtp().submitting()">
            {{ 'admin.save' | transloco }}
          </button>
          @if (canRemovePassword()) {
            <button
              uiButton
              variant="ghost"
              type="button"
              [loading]="removing()"
              (click)="removePassword()"
            >
              {{ 'admin.smtp.removePassword' | transloco }}
            </button>
          }
        </div>
      </form>

      <form class="test" [formRoot]="test" [attr.aria-label]="'admin.smtp.testTitle' | transloco">
        <h3>{{ 'admin.smtp.testTitle' | transloco }}</h3>
        @if (testProblem(); as key) {
          <ui-notice tone="error">
            {{ key | transloco }}
            @if (testDetail()) {
              <span class="detail">{{ testDetail() }}</span>
            }
          </ui-notice>
        } @else if (testSentTo(); as to) {
          <ui-notice tone="success">{{ 'admin.smtp.testSent' | transloco: { to } }}</ui-notice>
        }
        <div class="test-row">
          <ui-field
            [label]="'admin.smtp.testTo' | transloco"
            [error]="fieldErrorKey(test.to, testErrors) | transloco"
          >
            <input
              uiInput
              type="email"
              autocomplete="email"
              spellcheck="false"
              [formField]="test.to"
            />
          </ui-field>
          <button uiButton type="submit" [loading]="test().submitting()">
            {{ 'admin.smtp.testSend' | transloco }}
          </button>
        </div>
      </form>
    </ui-card>
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }
    .intro {
      margin-top: calc(-1 * var(--space-sm));
      margin-bottom: var(--space-lg);
      color: var(--muted);
    }
    .grid {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--space-lg) var(--gap);
    }
    .wide {
      grid-column: 1 / -1;
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-sm);
    }
    .test {
      display: grid;
      gap: var(--space-md);
      margin-top: var(--gap);
      padding-top: var(--space-xl);
      border-top: 1px solid var(--rule);
    }
    .test-row {
      display: grid;
      grid-template-columns: minmax(0, 1fr) auto;
      align-items: start;
      gap: var(--space-md);
    }
    .test-row > [uiButton] {
      margin-top: 26px;
    }
    .detail {
      display: block;
      margin-top: var(--space-xs);
      font-family: var(--mono);
      font-size: var(--fs-small);
      line-height: var(--lh-small);
      overflow-wrap: anywhere;
    }
    @media (max-width: 640px) {
      .grid,
      .test-row {
        grid-template-columns: minmax(0, 1fr);
      }
      .test-row > [uiButton] {
        margin-top: 0;
        justify-self: start;
      }
    }
  `,
})
export class SmtpCard {
  readonly settings = input.required<InstanceSettingsResponse>();
  /** The settings as the server stored them, after a save. */
  readonly changed = output<InstanceSettingsResponse>();

  private readonly admin = inject(AdministrationService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  protected readonly fieldErrorKey = fieldErrorKey;
  protected readonly securityModes = SMTP_SECURITY;
  protected readonly securityLabels: Record<string, string> = {
    starttls: 'admin.smtp.securityStarttls',
    tls: 'admin.smtp.securityTls',
    none: 'admin.smtp.securityNone',
  };
  protected readonly errors = {
    host: { server: 'admin.smtp.errors.host' },
    port: { pattern: 'admin.smtp.errors.port', server: 'admin.smtp.errors.port' },
    security: { server: 'admin.smtp.errors.security' },
    from: { email: 'admin.smtp.errors.from', server: 'admin.smtp.errors.from' },
  };
  protected readonly testErrors = {
    required: 'admin.smtp.errors.testTo',
    email: 'admin.smtp.errors.testTo',
    server: 'admin.smtp.errors.testTo',
  };

  protected readonly saveProblem = signal<string | null>(null);
  protected readonly saved = signal(false);
  protected readonly removing = signal(false);

  private readonly model = linkedSignal(() => smtpFormOf(this.settings()));
  protected readonly smtp = form(
    this.model,
    (p) => {
      pattern(p.port, /^([1-9]\d{0,4})?$/);
      email(p.from);
      disabled(p.host, () => this.settings().smtp.host.locked);
      disabled(p.port, () => this.settings().smtp.port.locked);
      disabled(p.user, () => this.settings().smtp.user.locked);
      disabled(p.newPassword, () => this.settings().smtp.password.locked);
      disabled(p.from, () => this.settings().smtp.from.locked);
      disabled(p.security, () => this.settings().smtp.security.locked);
    },
    {
      submission: {
        action: async (): Promise<TreeValidationResult> => {
          this.saveProblem.set(null);
          this.saved.set(false);
          const patch = smtpPatch(this.settings(), this.model());
          if (!hasChanges(patch)) {
            this.saved.set(true);
            return undefined;
          }
          try {
            this.changed.emit(await firstValueFrom(this.admin.updateInstanceSettings(patch)));
            this.saved.set(true);
            return undefined;
          } catch (error) {
            const onFields = fieldProblems(error, {
              smtpHost: this.smtp.host,
              smtpPort: this.smtp.port,
              smtpSecurity: this.smtp.security,
              smtpFrom: this.smtp.from,
            });
            if (onFields) {
              focusFirstInvalid(this.host);
              return onFields;
            }
            this.saveProblem.set(problemMessageKey(error, SAVE_MESSAGES));
            return undefined;
          }
        },
        onInvalid: () => focusFirstInvalid(this.host),
      },
    },
  );

  protected readonly canRemovePassword = computed(() => {
    const password = this.settings().smtp.password;
    return password.isSet && !password.locked;
  });

  protected readonly passwordHint = computed(() => {
    const password = this.settings().smtp.password;
    if (password.locked) return 'admin.locked';
    return password.isSet ? 'admin.smtp.passwordSet' : 'admin.smtp.passwordNotSet';
  });

  protected lockHint(setting: SettingView): string | null {
    return setting.locked ? 'admin.locked' : null;
  }

  protected async removePassword(): Promise<void> {
    this.saveProblem.set(null);
    this.saved.set(false);
    this.removing.set(true);
    try {
      this.changed.emit(
        await firstValueFrom(
          this.admin.updateInstanceSettings({ ...NO_CHANGES, smtpPassword: '' }),
        ),
      );
      this.saved.set(true);
    } catch (error) {
      this.saveProblem.set(problemMessageKey(error, SAVE_MESSAGES));
    } finally {
      this.removing.set(false);
    }
  }

  // The test e-mail
  protected readonly testProblem = signal<string | null>(null);
  protected readonly testDetail = signal<string | null>(null);
  protected readonly testSentTo = signal<string | null>(null);
  private readonly testModel = signal({ to: '' });
  protected readonly test = form(
    this.testModel,
    (p) => {
      email(p.to);
      required(p.to);
    },
    {
      submission: {
        action: async (): Promise<TreeValidationResult> => {
          this.testProblem.set(null);
          this.testDetail.set(null);
          this.testSentTo.set(null);
          const to = this.testModel().to.trim();
          try {
            await firstValueFrom(this.admin.sendTestEmail({ to }));
            this.testSentTo.set(to);
            return undefined;
          } catch (error) {
            const onField = fieldProblems(error, { to: this.test.to });
            if (onField) return onField;
            const key = problemMessageKey(error, {
              codes: {
                smtp_not_configured: 'admin.smtp.errors.notConfigured',
                smtp_failed: 'admin.smtp.errors.failed',
                setting_locked: 'admin.errors.locked',
              },
              fallback: 'admin.smtp.errors.failed',
            });
            this.testProblem.set(key);
            // The mail server's own answer helps the administrator find the cause.
            if (key === 'admin.smtp.errors.failed')
              this.testDetail.set(problemOf(error)?.detail ?? null);
            return undefined;
          }
        },
        onInvalid: () => focusFirstInvalid(this.host),
      },
    },
  );
}

const SAVE_MESSAGES = {
  codes: { setting_locked: 'admin.errors.locked', validation_failed: 'admin.errors.invalid' },
  fallback: 'admin.errors.saveFailed',
};
