import {
  Component,
  computed,
  ElementRef,
  inject,
  input,
  linkedSignal,
  signal,
} from '@angular/core';
import { disabled, form, FormField, FormRoot, maxLength, required } from '@angular/forms/signals';
import type { TreeValidationResult } from '@angular/forms/signals';
import { TranslocoPipe } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiCard, UiField, UiInput, UiNotice } from 'ui';
import {
  getOrganizationResource,
  OrganizationsService,
  type SessionOrganization,
} from '../../core/api/client';
import { problemMessageKey } from '../../core/api/problem';
import { Session } from '../../core/session/session';
import { fieldProblems } from '../admin/admin-errors';
import { fieldErrorKey } from '../auth/auth-forms';
import { focusFirstInvalid } from '../auth/focus-invalid';
import { canManage } from '../organizations/org-format';
import { TEAM_PROBLEMS } from '../team/team-problems';

export const LOCALES: readonly { value: string; name: string }[] = [
  { value: 'en', name: 'English' },
  { value: 'de', name: 'Deutsch' },
  { value: 'fr', name: 'Français' },
  { value: 'it', name: 'Italiano' },
];

function timeZones(): string[] {
  try {
    return Intl.supportedValuesOf('timeZone');
  } catch {
    return ['Europe/Zurich', 'Europe/Berlin', 'Europe/London', 'UTC'];
  }
}

@Component({
  selector: 'ardea-general-card',
  imports: [FormField, FormRoot, TranslocoPipe, UiButton, UiCard, UiField, UiInput, UiNotice],
  template: `
    <ui-card [heading]="'settings.general.title' | transloco">
      <form class="form" [formRoot]="general">
        <div class="live" role="status">
          @if (saved()) {
            <ui-notice tone="success">{{ 'settings.general.saved' | transloco }}</ui-notice>
          }
        </div>
        @if (problem()) {
          <ui-notice tone="error">{{ problem() | transloco }}</ui-notice>
        }
        <ui-field
          [label]="'settings.general.name' | transloco"
          [error]="fieldErrorKey(general.name, nameErrors) | transloco"
        >
          <input uiInput type="text" [formField]="general.name" />
        </ui-field>
        <ui-field
          [label]="'settings.general.locale' | transloco"
          [hint]="'settings.general.localeHint' | transloco"
        >
          <select uiInput [formField]="general.locale">
            @for (l of locales; track l.value) {
              <option [value]="l.value" [attr.lang]="l.value">{{ l.name }}</option>
            }
          </select>
        </ui-field>
        <ui-field
          [label]="'settings.general.timeZone' | transloco"
          [hint]="'settings.general.timeZoneHint' | transloco"
          [error]="fieldErrorKey(general.timeZone, zoneErrors) | transloco"
        >
          <select uiInput [formField]="general.timeZone">
            @for (zone of zones; track zone) {
              <option [value]="zone">{{ zone }}</option>
            }
          </select>
        </ui-field>
        @if (manage()) {
          <div>
            <button uiButton variant="primary" type="submit" [loading]="general().submitting()">
              {{ 'settings.save' | transloco }}
            </button>
          </div>
        } @else {
          <p class="hint">{{ 'settings.adminsOnly' | transloco }}</p>
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
    .live:empty {
      display: none;
    }
    .hint {
      color: var(--muted);
      font-size: var(--fs-sub);
    }
  `,
})
export class GeneralCard {
  readonly organization = input.required<SessionOrganization>();

  private readonly api = inject(OrganizationsService);
  private readonly session = inject(Session);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  protected readonly fieldErrorKey = fieldErrorKey;
  protected readonly nameErrors = {
    required: 'organizations.create.errors.nameRequired',
    maxLength: 'organizations.create.errors.nameTooLong',
    server: 'organizations.create.errors.nameRequired',
  };
  protected readonly zoneErrors = { server: 'settings.general.timeZoneInvalid' };
  protected readonly locales = LOCALES;
  protected readonly zones = timeZones();
  protected readonly manage = computed(() => canManage(this.organization().role));
  protected readonly saved = signal(false);
  protected readonly problem = signal<string | null>(null);

  private readonly current = getOrganizationResource(computed(() => this.organization().id));
  private readonly model = linkedSignal(() => {
    const org = this.current.hasValue() ? this.current.value() : undefined;
    return {
      name: org?.name ?? this.organization().name,
      locale: org?.locale ?? 'en',
      timeZone: org?.timeZone ?? 'Europe/Zurich',
    };
  });

  protected readonly general = form(
    this.model,
    (p) => {
      required(p.name);
      maxLength(p.name, 200);
      disabled(p, () => !this.manage());
    },
    {
      submission: {
        action: async (): Promise<TreeValidationResult> => {
          this.problem.set(null);
          this.saved.set(false);
          const m = this.model();
          try {
            await firstValueFrom(
              this.api.updateOrganization(this.organization().id, {
                name: m.name.trim(),
                locale: m.locale,
                timeZone: m.timeZone,
                requireMfa: null,
                aiPolicy: null,
              }),
            );
            this.saved.set(true);
            this.current.reload();
            await this.session.refresh();
            return undefined;
          } catch (error) {
            const onField = fieldProblems(error, {
              name: this.general.name,
              locale: this.general.locale,
              timeZone: this.general.timeZone,
            });
            if (onField) {
              focusFirstInvalid(this.host);
              return onField;
            }
            this.problem.set(
              problemMessageKey(error, { codes: TEAM_PROBLEMS, fallback: 'team.errors.failed' }),
            );
            return undefined;
          }
        },
        onInvalid: () => focusFirstInvalid(this.host),
      },
    },
  );
}
