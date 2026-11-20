import {
  Component,
  computed,
  ElementRef,
  inject,
  input,
  linkedSignal,
  signal,
} from '@angular/core';
import { form, FormField, FormRoot, maxLength, required } from '@angular/forms/signals';
import type { TreeValidationResult } from '@angular/forms/signals';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiCard, UiField, UiInput, UiNotice, UiToaster } from 'ui';
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
      @if (!manage()) {
        <dl class="pairs">
          <div>
            <dt>{{ 'settings.general.name' | transloco }}</dt>
            <dd>{{ model().name }}</dd>
          </div>
          <div>
            <dt>{{ 'settings.general.locale' | transloco }}</dt>
            <dd [attr.lang]="model().locale">{{ localeName() }}</dd>
          </div>
          <div>
            <dt>{{ 'settings.general.timeZone' | transloco }}</dt>
            <dd>{{ model().timeZone }}</dd>
          </div>
        </dl>
        <p class="hint">{{ 'settings.adminsOnly' | transloco }}</p>
      } @else {
        <form class="form" [formRoot]="general">
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
          <div>
            <button uiButton variant="primary" type="submit" [loading]="general().submitting()">
              {{ 'settings.save' | transloco }}
            </button>
          </div>
        </form>
      }
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
    .hint {
      color: var(--muted);
      font-size: var(--fs-sub);
    }
    .pairs {
      display: grid;
      gap: var(--space-sm);
      margin-bottom: var(--space-md);
    }
    .pairs > div {
      display: grid;
      grid-template-columns: minmax(0, 2fr) minmax(0, 3fr);
      gap: var(--space-md);
      padding-top: var(--space-sm);
      border-top: 1px solid var(--rule);
    }
    .pairs > div:first-child {
      padding-top: 0;
      border-top: 0;
    }
    .pairs dt {
      color: var(--muted);
    }
    .pairs dd {
      min-width: 0;
      overflow-wrap: anywhere;
    }
  `,
})
export class GeneralCard {
  readonly organization = input.required<SessionOrganization>();

  private readonly api = inject(OrganizationsService);
  private readonly session = inject(Session);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly transloco = inject(TranslocoService);
  private readonly toaster = inject(UiToaster);

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
  protected readonly localeName = computed(
    () => LOCALES.find((l) => l.value === this.model().locale)?.name ?? this.model().locale,
  );
  protected readonly problem = signal<string | null>(null);

  private readonly current = getOrganizationResource(computed(() => this.organization().id));
  protected readonly model = linkedSignal(() => {
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
    },
    {
      submission: {
        action: async (): Promise<TreeValidationResult> => {
          this.problem.set(null);
          const stored = this.current.hasValue() ? this.current.value() : undefined;
          const before = stored
            ? { name: stored.name, locale: stored.locale, timeZone: stored.timeZone }
            : null;
          try {
            await this.apply({ ...this.model(), name: this.model().name.trim() });
            this.toaster.show({
              text: this.transloco.translate('settings.general.saved'),
              undo: before ? () => void this.restore(before) : undefined,
            });
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

  private async apply(values: { name: string; locale: string; timeZone: string }): Promise<void> {
    await firstValueFrom(
      this.api.updateOrganization(this.organization().id, {
        ...values,
        requireMfa: null,
        aiPolicy: null,
      }),
    );
    this.current.reload();
    await this.session.refresh();
  }

  private async restore(values: { name: string; locale: string; timeZone: string }): Promise<void> {
    this.problem.set(null);
    try {
      await this.apply(values);
    } catch (error) {
      this.problem.set(
        problemMessageKey(error, { codes: TEAM_PROBLEMS, fallback: 'team.errors.failed' }),
      );
    }
  }
}
