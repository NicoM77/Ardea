import { Component, input } from '@angular/core';

export type UiPillTone = 'neutral' | 'alert' | 'done';

@Component({
  selector: 'ui-pill',
  host: { '[class]': "'is-' + tone()" },
  template: `<ng-content />`,
  styles: `
    :host {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      flex: none;
      height: 22px;
      padding: 0 var(--space-sm);
      border-radius: var(--radius-pill);
      background: var(--raised);
      color: var(--text);
      font-size: var(--fs-small);
      line-height: var(--lh-small);
      font-weight: 500;
      white-space: nowrap;
    }
    :host(.is-alert) {
      background: var(--alert-soft);
      color: var(--alert-ink);
    }
    :host(.is-alert)::before {
      content: '';
      width: 6px;
      height: 6px;
      border-radius: 50%;
      background: var(--critical);
    }
    :host(.is-done) {
      background: var(--positive-soft);
      color: var(--positive-ink);
    }
  `,
})
export class UiPill {
  readonly tone = input<UiPillTone>('neutral');
}
