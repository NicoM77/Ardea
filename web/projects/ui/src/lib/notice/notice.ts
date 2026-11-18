import { Component, input } from '@angular/core';
import { LucideCircleExclamationPoint, LucideCircleCheck, LucideInfo } from '@lucide/angular';

export type UiNoticeTone = 'info' | 'error' | 'success';

/**
 * A sentence about the page or the form as a whole: a failed sign-in, a sent link, a step done.
 * Errors stay neutral (red means Critical); the icon and the sentence carry the state. An error
 * notice is an alert, so screen readers hear it the moment it appears; the others are status.
 */
@Component({
  selector: 'ui-notice',
  imports: [LucideCircleExclamationPoint, LucideCircleCheck, LucideInfo],
  host: {
    '[class]': "'is-' + tone()",
    '[attr.role]': "tone() === 'error' ? 'alert' : 'status'",
  },
  template: `
    @switch (tone()) {
      @case ('error') {
        <svg lucideCircleExclamationPoint aria-hidden="true"></svg>
      }
      @case ('success') {
        <svg lucideCircleCheck aria-hidden="true"></svg>
      }
      @default {
        <svg lucideInfo aria-hidden="true"></svg>
      }
    }
    <div class="body">
      @if (heading()) {
        <p class="heading">{{ heading() }}</p>
      }
      <div class="text"><ng-content /></div>
    </div>
  `,
  styles: `
    :host {
      display: flex;
      align-items: flex-start;
      gap: 10px;
      padding: 10px var(--space-md);
      border: 1px solid var(--border);
      border-radius: var(--radius-card);
      background: var(--raised);
      color: var(--text);
    }
    :host(.is-error) {
      border-color: var(--field);
      background: var(--surface);
    }
    :host(.is-success) {
      border-color: transparent;
      background: var(--positive-soft);
      color: var(--positive-ink);
    }
    svg {
      width: 16px;
      height: 16px;
      margin-top: 2px;
      color: var(--muted);
    }
    :host(.is-error) svg {
      color: var(--text);
    }
    :host(.is-success) svg {
      color: var(--positive);
    }
    .body {
      min-width: 0;
      display: grid;
      gap: 2px;
    }
    .heading {
      font-weight: 500;
    }
    .text {
      overflow-wrap: anywhere;
    }
    :host(:not(.is-success)) .heading + .text {
      color: var(--muted);
    }
  `,
})
export class UiNotice {
  readonly tone = input<UiNoticeTone>('info');
  readonly heading = input<string | null>(null);
}
