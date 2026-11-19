import { Component, computed, input, linkedSignal, model, output, signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { UiButton, UiCard, UiNotice } from 'ui';
import type { ProjectView } from '../../core/api/client';
import { CatalogPicker, type Picked } from '../projects/catalog-picker';
import {
  addChoice,
  CRITICALITIES,
  CRITICALITY_HINTS,
  CRITICALITY_LABELS,
  EXPOSURE_HINTS,
  EXPOSURE_LABELS,
  EXPOSURES,
  withoutDraft,
  type DraftComponent,
} from '../projects/component-format';
import { ChoiceGroup, type ChoiceOption } from './choice-group';

export interface OperationsAnswer {
  exposure: string;
  criticality: string;
}

const HOSTS: readonly ChoiceOption[] = [
  {
    value: 'server',
    label: 'wizard.operations.hosts.server',
    hint: 'wizard.operations.hosts.serverHint',
  },
  {
    value: 'containers',
    label: 'wizard.operations.hosts.containers',
    hint: 'wizard.operations.hosts.containersHint',
  },
  {
    value: 'managed',
    label: 'wizard.operations.hosts.managed',
    hint: 'wizard.operations.hosts.managedHint',
  },
  { value: 'unknown', label: 'wizard.operations.hosts.unknown' },
];

/** What each answer asks next, and which kinds of the catalogue fit the answer. */
const FOLLOW_UP: Record<string, { label: string; kinds: readonly string[] }> = {
  server: { label: 'wizard.operations.which.os', kinds: ['os'] },
  containers: {
    label: 'wizard.operations.which.platform',
    kinds: ['container', 'virtualization', 'service'],
  },
  managed: { label: 'wizard.operations.which.service', kinds: ['service'] },
};

/**
 * Step "Operations" (master plan 11.6): what the repository cannot answer, asked while it is read.
 * Exposure and criticality are saved on the project; where it runs and what sits in front become
 * components entered by hand. "Don't know" is always valid.
 */
@Component({
  selector: 'ardea-operations-step',
  imports: [TranslocoPipe, UiButton, UiCard, UiNotice, CatalogPicker, ChoiceGroup],
  template: `
    <div class="stack">
      <ui-card [heading]="'wizard.operations.reachTitle' | transloco">
        <div class="questions">
          <ardea-choice-group
            [legend]="'wizard.operations.exposure' | transloco"
            [hint]="'wizard.operations.exposureHint' | transloco"
            [options]="exposures"
            [(value)]="exposure"
          />
          <ardea-choice-group
            [legend]="'wizard.operations.criticality' | transloco"
            [hint]="'wizard.operations.criticalityHint' | transloco"
            [options]="criticalities"
            [(value)]="criticality"
          />
        </div>
      </ui-card>

      <ui-card [heading]="'wizard.operations.hostingTitle' | transloco">
        <div class="questions">
          <ardea-choice-group
            [legend]="'wizard.operations.host' | transloco"
            [options]="hosts"
            [(value)]="host"
          />
          @if (followUp(); as next) {
            <div class="question">
              <ardea-catalog-picker
                [kinds]="next.kinds"
                [label]="next.label | transloco"
                [placeholder]="next.label | transloco"
                (picked)="add($event)"
              />
            </div>
          }
          <div class="question">
            <p class="legend">{{ 'wizard.operations.front' | transloco }}</p>
            <ardea-catalog-picker
              [kinds]="frontKinds"
              [label]="'wizard.operations.front' | transloco"
              [placeholder]="'wizard.operations.frontPlaceholder' | transloco"
              (picked)="add($event)"
            />
          </div>
          @if (addedHere().length) {
            <div class="question" role="status">
              <p class="legend">{{ 'wizard.operations.added' | transloco }}</p>
              <ul class="added">
                @for (draft of addedHere(); track draft.ref) {
                  <li>
                    <span>{{ draft.name }}</span>
                    <button
                      uiButton
                      variant="ghost"
                      type="button"
                      [attr.aria-label]="
                        'projects.drafts.removeNamed' | transloco: { name: draft.name }
                      "
                      (click)="remove(draft)"
                    >
                      {{ 'projects.drafts.remove' | transloco }}
                    </button>
                  </li>
                }
              </ul>
              <p class="hint">{{ 'wizard.operations.addedHint' | transloco }}</p>
            </div>
          }
        </div>
      </ui-card>

      @if (problem()) {
        <ui-notice tone="error">{{ problem() | transloco }}</ui-notice>
      }
      <div class="actions">
        <button uiButton variant="ghost" type="button" (click)="back.emit()">
          {{ 'wizard.back' | transloco }}
        </button>
        <button
          uiButton
          variant="primary"
          type="button"
          [loading]="saving()"
          (click)="
            continued.emit({
              exposure: exposure() ?? 'internal',
              criticality: criticality() ?? 'normal',
            })
          "
        >
          {{ 'wizard.continue' | transloco }}
        </button>
      </div>
    </div>
  `,
  styles: `
    :host {
      display: block;
    }
    .stack {
      display: grid;
      gap: var(--gap);
    }
    .questions {
      display: grid;
      gap: var(--space-xl);
    }
    .question {
      display: grid;
      gap: 10px;
    }
    .legend {
      font-weight: 500;
    }
    .hint {
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .added li {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-sm);
      min-height: 40px;
      border-top: 1px solid var(--rule);
    }
    .added li:first-child {
      border-top: 0;
    }
    .actions {
      display: flex;
      justify-content: space-between;
      gap: var(--space-md);
      padding-top: var(--space-lg);
      border-top: 1px solid var(--border);
    }
  `,
})
export class OperationsStep {
  readonly project = input.required<ProjectView>();
  readonly drafts = model.required<DraftComponent[]>();
  readonly saving = input(false);
  readonly problem = input<string | null>(null);
  readonly continued = output<OperationsAnswer>();
  readonly back = output<void>();

  protected readonly exposures: ChoiceOption[] = EXPOSURES.map((v) => ({
    value: v,
    label: EXPOSURE_LABELS[v],
    hint: EXPOSURE_HINTS[v],
  }));
  protected readonly criticalities: ChoiceOption[] = CRITICALITIES.map((v) => ({
    value: v,
    label: CRITICALITY_LABELS[v],
    hint: CRITICALITY_HINTS[v],
  }));
  protected readonly hosts = HOSTS;
  protected readonly frontKinds = ['network', 'service'];

  protected readonly exposure = linkedSignal<string | null>(() => this.project().exposure);
  protected readonly criticality = linkedSignal<string | null>(() => this.project().criticality);
  protected readonly host = signal<string | null>(null);
  protected readonly followUp = computed(() => FOLLOW_UP[this.host() ?? ''] ?? null);

  private readonly added = signal<string[]>([]);
  protected readonly addedHere = computed(() =>
    this.drafts().filter((d) => this.added().includes(d.ref)),
  );

  protected add(choice: Picked): void {
    const { list, draft } = addChoice(this.drafts(), choice);
    this.drafts.set(list);
    this.added.update((refs) => (refs.includes(draft.ref) ? refs : [...refs, draft.ref]));
  }

  protected remove(draft: DraftComponent): void {
    this.drafts.update((list) => withoutDraft(list, draft.ref));
  }
}
