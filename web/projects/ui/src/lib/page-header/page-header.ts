import { Component, input } from '@angular/core';

@Component({
  selector: 'ui-page-header',
  template: `
    <div class="text">
      <h1>{{ heading() }}</h1>
      @if (sub()) {
        <p class="sub">{{ sub() }}</p>
      }
    </div>
    <div class="actions"><ng-content select="[uiPageActions]" /></div>
  `,
  styles: `
    :host {
      display: flex;
      align-items: flex-end;
      justify-content: space-between;
      flex-wrap: wrap;
      gap: var(--space-lg) var(--gap);
      margin-bottom: var(--space-xl);
    }
    .text {
      min-width: 0;
    }
    .sub {
      margin-top: var(--space-xs);
      color: var(--muted);
      max-width: 72ch;
    }
    .actions {
      display: flex;
      align-items: center;
      flex-wrap: wrap;
      gap: var(--space-sm);
    }
    .actions:empty {
      display: none;
    }
    @media (max-width: 640px) {
      :host {
        align-items: flex-start;
        margin-bottom: var(--space-lg);
      }
    }
  `,
})
export class UiPageHeader {
  readonly heading = input.required<string>();
  readonly sub = input<string | null>(null);
}
