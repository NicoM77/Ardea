import { Component, computed, inject } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { UiCard, UiLoader, UiNotice, UiPageHeader } from 'ui';
import { StackEntries } from './stack-entries';
import { StackStore } from './stack-store';
import { StackSuggestions } from './stack-suggestions';
import { StackThresholds } from './stack-thresholds';

@Component({
  selector: 'ardea-stack-page',
  imports: [
    TranslocoPipe,
    UiCard,
    UiLoader,
    UiNotice,
    UiPageHeader,
    StackEntries,
    StackSuggestions,
    StackThresholds,
  ],
  providers: [StackStore],
  template: `
    <ui-page-header [heading]="'nav.stack' | transloco" [sub]="'stack.lead' | transloco" />

    @if (store.loaded()) {
      <p class="summary">
        {{ 'stack.summary' | transloco: { count: count(), lines: lines() } }}
      </p>
      <div class="grid">
        <div class="main">
          <ui-card [heading]="'stack.entriesTitle' | transloco">
            <span uiCardMeta>{{ 'stack.savedAtOnce' | transloco }}</span>
            @if (store.problem(); as problem) {
              <ui-notice class="problem" tone="error">{{ problem | transloco }}</ui-notice>
            }
            <ardea-stack-entries />
          </ui-card>
        </div>
        <div class="side">
          @if (store.canEdit()) {
            <ui-card [heading]="'stack.suggest.title' | transloco">
              <ardea-stack-suggestions />
            </ui-card>
          }
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
    .summary {
      margin: calc(-1 * var(--space-md)) 0 var(--space-lg);
      color: var(--muted);
    }
    .grid {
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
    .problem {
      display: block;
      margin-bottom: var(--space-lg);
    }
    @media (max-width: 1100px) {
      .grid {
        grid-template-columns: minmax(0, 1fr);
      }
    }
  `,
})
export class StackPage {
  protected readonly store = inject(StackStore);
  protected readonly count = computed(() => this.store.entries().length);
  protected readonly lines = computed(
    () => this.store.entries().filter((e) => e.versionPrecision === 'line').length,
  );
}
