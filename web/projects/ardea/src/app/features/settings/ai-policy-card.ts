import { Component, computed, inject, input, linkedSignal, signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiCard, UiNotice } from 'ui';
import {
  getOrganizationResource,
  OrganizationsService,
  type SessionOrganization,
} from '../../core/api/client';
import { problemMessageKey } from '../../core/api/problem';
import { ChoiceGroup, type ChoiceOption } from '../setup/choice-group';
import { TEAM_PROBLEMS } from '../team/team-problems';

export const AI_POLICIES: readonly ChoiceOption[] = [
  { value: 'none', label: 'settings.ai.none', hint: 'settings.ai.noneHint' },
  { value: 'local_only', label: 'settings.ai.localOnly', hint: 'settings.ai.localOnlyHint' },
  {
    value: 'cloud_allowed',
    label: 'settings.ai.cloudAllowed',
    hint: 'settings.ai.cloudAllowedHint',
  },
];

@Component({
  selector: 'ardea-ai-policy-card',
  imports: [TranslocoPipe, UiButton, UiCard, UiNotice, ChoiceGroup],
  template: `
    <ui-card [heading]="'settings.ai.title' | transloco">
      <p class="intro">{{ 'settings.ai.intro' | transloco }}</p>
      <div class="live" role="status">
        @if (saved()) {
          <ui-notice tone="success">{{ 'settings.ai.saved' | transloco }}</ui-notice>
        }
      </div>
      @if (problem(); as key) {
        <ui-notice class="gap" tone="error">{{ key | transloco }}</ui-notice>
      }
      @if (loaded()) {
        <ardea-choice-group legend="settings.ai.legend" [options]="options" [(value)]="policy" />
        <p class="hint">{{ 'settings.ai.leaves' | transloco }}</p>
        <div class="actions">
          <button
            uiButton
            type="button"
            [disabled]="!changed()"
            [loading]="busy()"
            (click)="save()"
          >
            {{ 'settings.save' | transloco }}
          </button>
        </div>
      } @else if (current.error()) {
        <ui-notice tone="error">{{ 'settings.ai.loadFailed' | transloco }}</ui-notice>
      }
    </ui-card>
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }
    .intro,
    .hint {
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .intro {
      margin-bottom: var(--space-lg);
    }
    .hint {
      margin-top: var(--space-md);
    }
    .live:empty {
      display: none;
    }
    .live,
    .gap {
      margin-bottom: var(--space-md);
    }
    .actions {
      margin-top: var(--space-lg);
    }
  `,
})
export class AiPolicyCard {
  readonly organization = input.required<SessionOrganization>();

  private readonly api = inject(OrganizationsService);
  protected readonly options = AI_POLICIES;
  protected readonly current = getOrganizationResource(computed(() => this.organization().id));
  private readonly stored = computed(() =>
    this.current.hasValue() ? (this.current.value()?.aiPolicy ?? null) : null,
  );
  protected readonly loaded = computed(() => this.stored() !== null);
  protected readonly policy = linkedSignal<string | null>(() => this.stored() ?? 'local_only');
  protected readonly changed = computed(() => this.loaded() && this.policy() !== this.stored());
  protected readonly busy = signal(false);
  protected readonly saved = signal(false);
  protected readonly problem = signal<string | null>(null);

  protected async save(): Promise<void> {
    this.busy.set(true);
    this.saved.set(false);
    this.problem.set(null);
    try {
      const view = await firstValueFrom(
        this.api.updateOrganization(this.organization().id, {
          name: null,
          locale: null,
          timeZone: null,
          requireMfa: null,
          aiPolicy: this.policy(),
        }),
      );
      this.current.set(view);
      this.saved.set(true);
    } catch (error) {
      this.problem.set(
        problemMessageKey(error, { codes: TEAM_PROBLEMS, fallback: 'team.errors.failed' }),
      );
    } finally {
      this.busy.set(false);
    }
  }
}
