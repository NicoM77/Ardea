import { Component, input } from '@angular/core';

let nextCard = 0;

@Component({
  selector: 'ui-card',
  template: `
    @if (heading()) {
      <header>
        <h2 [id]="headingId()">{{ heading() }}</h2>
        <div class="meta"><ng-content select="[uiCardMeta]" /></div>
      </header>
    }
    <ng-content />
  `,
  host: {
    '[attr.role]': "heading() ? 'region' : null",
    '[attr.aria-labelledby]': 'heading() ? headingId() : null',
  },
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      min-width: 0;
      padding: var(--card-pad);
      background: var(--surface);
      border: 1px solid var(--border);
      border-radius: var(--radius-card);
    }
    header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-md);
      min-height: 24px;
      margin-bottom: var(--space-lg);
    }
    .meta {
      display: flex;
      align-items: center;
      gap: var(--space-sm);
      color: var(--faint);
      font-size: var(--fs-small);
      white-space: nowrap;
    }
    .meta:empty {
      display: none;
    }
  `,
})
export class UiCard {
  readonly heading = input<string | null>(null);
  readonly headingId = input(`ui-card-${nextCard++}`);
}
