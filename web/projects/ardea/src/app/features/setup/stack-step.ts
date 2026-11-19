import { Component, computed, inject, input, output, viewChild } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { UiButton, UiCard, UiLoader, UiNotice } from 'ui';
import { StackEntries } from '../stack/stack-entries';
import { TYPICAL_STACK } from '../stack/stack-format';
import { StackStore } from '../stack/stack-store';
import { StackSuggestions } from '../stack/stack-suggestions';
import { StackThresholds } from '../stack/stack-thresholds';

/**
 * Step "Your stack" (master plan 11.6 step 1b, 17.2; task 5a.4): the products the organization
 * runs, picked from the catalogue without versions, each with an optional version line and its
 * threshold. It is the one step that matters for notices; projects and repositories are optional
 * after it. Every change is saved at once, so Continue only records the step.
 */
@Component({
  selector: 'ardea-stack-step',
  imports: [
    TranslocoPipe,
    UiButton,
    UiCard,
    UiLoader,
    UiNotice,
    StackEntries,
    StackSuggestions,
    StackThresholds,
  ],
  providers: [StackStore],
  template: `
    @if (store.loaded()) {
      <div class="layout">
        <div class="main">
          <ui-card [heading]="'wizard.stack.title' | transloco">
            <span uiCardMeta>{{ 'wizard.stack.count' | transloco: { count: count() } }}</span>
            <div class="body">
              <p class="hint">{{ 'wizard.stack.lead' | transloco }}</p>
              <p class="hint">{{ 'wizard.stack.notice' | transloco }}</p>
              @if (store.problem(); as problem) {
                <ui-notice tone="error">{{ problem | transloco }}</ui-notice>
              }
              <ardea-stack-entries />
              <!-- Always in the page so the sentence is heard when it appears; empty otherwise. -->
              <p class="hint typical" role="status">
                @if (count() > 0 && count() < typical.min) {
                  {{ 'wizard.stack.typical' | transloco: typical }}
                }
              </p>
            </div>
          </ui-card>

          @if (problem(); as problem) {
            <ui-notice tone="error">{{ problem | transloco }}</ui-notice>
          }
          <div class="actions">
            <span></span>
            <button uiButton variant="primary" type="button" [loading]="saving()" (click)="next()">
              {{ 'wizard.continue' | transloco }}
            </button>
          </div>
        </div>

        <div class="side">
          <ui-card [heading]="'stack.suggest.title' | transloco">
            <ardea-stack-suggestions />
          </ui-card>
          <ui-card [heading]="'stack.explain.title' | transloco">
            <ardea-stack-thresholds />
          </ui-card>
        </div>
      </div>
    } @else if (store.loadProblem(); as problem) {
      <ui-notice tone="error">{{ problem | transloco }}</ui-notice>
    } @else {
      <ui-loader [label]="'stack.loading' | transloco" />
    }
  `,
  styles: `
    :host {
      display: block;
    }
    .layout {
      display: grid;
      grid-template-columns: minmax(0, 8fr) minmax(0, 4fr);
      gap: var(--gap);
      align-items: start;
    }
    .main,
    .side {
      display: grid;
      gap: var(--gap);
      min-width: 0;
    }
    .body {
      display: grid;
      gap: var(--space-lg);
    }
    .hint {
      color: var(--muted);
      max-width: 72ch;
    }
    .typical:empty {
      margin-top: calc(-1 * var(--space-lg));
    }
    .actions {
      display: flex;
      justify-content: space-between;
      gap: var(--space-md);
      padding-top: var(--space-lg);
      border-top: 1px solid var(--border);
    }
    @media (max-width: 1100px) {
      .layout {
        grid-template-columns: minmax(0, 1fr);
      }
    }
  `,
})
export class StackStep {
  readonly saving = input(false);
  /** Set by the wizard when recording the step failed. */
  readonly problem = input<string | null>(null);
  readonly continued = output();

  protected readonly store = inject(StackStore);
  private readonly entries = viewChild(StackEntries);
  protected readonly typical = TYPICAL_STACK;
  protected readonly count = computed(() => this.store.entries().length);

  /** The stack needs one product at least; without one the step says so and puts the focus in the search. */
  protected next(): void {
    if (!this.count()) {
      this.store.problem.set('wizard.stack.empty');
      this.entries()?.focusSearch();
      return;
    }
    this.continued.emit();
  }
}
