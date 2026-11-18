import { Component, computed, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

export type UiStatusKind = 'open' | 'review' | 'remediated' | 'accepted' | 'notAffected';

export const UI_STATUS_KINDS: readonly UiStatusKind[] = [
  'open',
  'review',
  'remediated',
  'accepted',
  'notAffected',
];

const STATUS_LABELS: Record<UiStatusKind, string> = {
  open: 'ui.status.open',
  review: 'ui.status.review',
  remediated: 'ui.status.remediated',
  accepted: 'ui.status.accepted',
  notAffected: 'ui.status.notAffected',
};

/**
 * Working state (DESIGN.md): a neutral symbol plus the word. Empty ring Open, half-filled ring
 * In review, green check disc Remediated, flag Risk accepted, struck ring Not affected.
 */
@Component({
  selector: 'ui-status',
  imports: [TranslocoPipe],
  host: { '[class]': "'is-' + kind()" },
  template: `
    <svg viewBox="0 0 24 24" aria-hidden="true">
      @switch (kind()) {
        @case ('open') {
          <circle class="line" cx="12" cy="12" r="8.4" />
        }
        @case ('review') {
          <circle class="line" cx="12" cy="12" r="8.4" />
          <path class="fill" d="M12 6.4a5.6 5.6 0 0 1 0 11.2Z" />
        }
        @case ('remediated') {
          <circle class="fill" cx="12" cy="12" r="9.5" />
          <path class="check" d="m8 12.3 2.8 2.8 5.2-5.6" />
        }
        @case ('accepted') {
          <path class="line" d="M5.5 21V4.5M5.5 4.5h12l-2.6 4.2 2.6 4.3h-12" />
        }
        @default {
          <circle class="line" cx="12" cy="12" r="8.4" />
          <path class="line" d="m6.2 17.8 11.6-11.6" />
        }
      }
    </svg>
    <span>{{ label() | transloco }}</span>
  `,
  styles: `
    :host {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      white-space: nowrap;
    }
    svg {
      width: 1em;
      height: 1em;
      flex: none;
      color: var(--muted);
    }
    :host(.is-remediated) svg {
      color: var(--positive);
    }
    .line {
      fill: none;
      stroke: currentColor;
      stroke-width: 2.2;
      stroke-linecap: round;
      stroke-linejoin: round;
    }
    .fill {
      fill: currentColor;
    }
    .check {
      fill: none;
      stroke: var(--surface);
      stroke-width: 2.2;
      stroke-linecap: round;
      stroke-linejoin: round;
    }
  `,
})
export class UiStatus {
  readonly kind = input.required<UiStatusKind>();
  protected readonly label = computed(() => STATUS_LABELS[this.kind()]);
}
