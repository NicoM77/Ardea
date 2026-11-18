import { Component, input } from '@angular/core';

/**
 * What a page or list shows when there is nothing in it yet. Says what will appear here and, when
 * there is one, the step that makes it appear. Silence is a valid state (PRODUCT.md, principle 1),
 * so the tone is calm: a dashed edge, muted words, no illustration.
 *
 * Slots: an icon (`[uiEmptyIcon]`), the text (default) and actions (`[uiEmptyActions]`).
 */
@Component({
  selector: 'ui-empty-state',
  template: `
    <div class="icon"><ng-content select="[uiEmptyIcon]" /></div>
    <p class="heading">{{ heading() }}</p>
    <div class="text"><ng-content /></div>
    <div class="actions"><ng-content select="[uiEmptyActions]" /></div>
  `,
  styles: `
    :host {
      display: grid;
      justify-items: center;
      align-content: center;
      gap: var(--space-sm);
      min-height: 200px;
      padding: var(--gap);
      border: 1px dashed var(--control);
      border-radius: var(--radius-card);
      color: var(--muted);
      text-align: center;
    }
    .icon {
      display: grid;
      place-items: center;
      color: var(--faint);
    }
    .icon:empty,
    .actions:empty {
      display: none;
    }
    .icon ::ng-deep svg {
      width: 20px;
      height: 20px;
    }
    .heading {
      color: var(--text);
      font-size: var(--fs-body);
      line-height: var(--lh-body);
      font-weight: 500;
    }
    .text {
      max-width: 52ch;
      text-wrap: pretty;
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      justify-content: center;
      gap: var(--space-sm);
      margin-top: var(--space-sm);
    }
  `,
})
export class UiEmptyState {
  readonly heading = input.required<string>();
}
