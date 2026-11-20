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
import { disabled, form, FormField, FormRoot } from '@angular/forms/signals';
import type { TreeValidationResult } from '@angular/forms/signals';
import { TranslocoPipe } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiCard, UiField, UiInput, UiNotice } from 'ui';
import { AdministrationService, type InstanceSettingsResponse } from '../../core/api/client';
import { problemMessageKey } from '../../core/api/problem';
import { fieldErrorKey } from '../auth/auth-forms';
import { focusFirstInvalid } from '../auth/focus-invalid';
import { fieldProblems } from './admin-errors';
import { NO_CHANGES, REGISTRATION_MODES } from './settings-patch';

const MODE_LABELS: Record<string, string> = {
  invite: 'admin.registration.invite',
  open: 'admin.registration.open',
  closed: 'admin.registration.closed',
};
const MODE_HINTS: Record<string, string> = {
  invite: 'admin.registration.inviteHint',
  open: 'admin.registration.openHint',
  closed: 'admin.registration.closedHint',
};

@Component({
  selector: 'ardea-registration-card',
  imports: [FormField, FormRoot, TranslocoPipe, UiButton, UiCard, UiField, UiInput, UiNotice],
  template: `
    <ui-card [heading]="'admin.registration.title' | transloco">
      <form class="form" [formRoot]="registration">
        @if (problem()) {
          <ui-notice tone="error">{{ problem() | transloco }}</ui-notice>
        } @else if (saved()) {
          <ui-notice tone="success">{{ 'admin.registration.saved' | transloco }}</ui-notice>
        }
        <ui-field
          [label]="'admin.registration.mode' | transloco"
          [hint]="hint() | transloco"
          [error]="fieldErrorKey(registration.mode, errors) | transloco"
        >
          <select uiInput [formField]="registration.mode">
            @for (mode of modes; track mode) {
              <option [value]="mode">{{ labels[mode] | transloco }}</option>
            }
          </select>
        </ui-field>
        @if (!locked()) {
          <div>
            <button
              uiButton
              variant="primary"
              type="submit"
              [loading]="registration().submitting()"
            >
              {{ 'admin.save' | transloco }}
            </button>
          </div>
        }
      </form>
    </ui-card>
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }
    .form {
      display: grid;
      gap: var(--space-lg);
    }
  `,
})
export class RegistrationCard {
  readonly settings = input.required<InstanceSettingsResponse>();
  readonly changed = output<InstanceSettingsResponse>();

  private readonly admin = inject(AdministrationService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  protected readonly fieldErrorKey = fieldErrorKey;
  protected readonly modes = REGISTRATION_MODES;
  protected readonly labels = MODE_LABELS;
  protected readonly errors = { server: 'admin.registration.errors.mode' };
  protected readonly problem = signal<string | null>(null);
  protected readonly saved = signal(false);
  protected readonly locked = computed(() => this.settings().registration.locked);

  private readonly model = linkedSignal(() => ({
    mode: this.settings().registration.value ?? 'invite',
  }));
  protected readonly hint = computed(() =>
    this.locked() ? 'admin.locked' : (MODE_HINTS[this.model().mode] ?? null),
  );

  protected readonly registration = form(this.model, (p) => disabled(p.mode, () => this.locked()), {
    submission: {
      action: async (): Promise<TreeValidationResult> => {
        this.problem.set(null);
        this.saved.set(false);
        try {
          this.changed.emit(
            await firstValueFrom(
              this.admin.updateInstanceSettings({ ...NO_CHANGES, registration: this.model().mode }),
            ),
          );
          this.saved.set(true);
          return undefined;
        } catch (error) {
          const onField = fieldProblems(error, { registration: this.registration.mode });
          if (onField) {
            focusFirstInvalid(this.host);
            return onField;
          }
          this.problem.set(
            problemMessageKey(error, {
              codes: { setting_locked: 'admin.errors.locked' },
              fallback: 'admin.errors.saveFailed',
            }),
          );
          return undefined;
        }
      },
    },
  });
}
