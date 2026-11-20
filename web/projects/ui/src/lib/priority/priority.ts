import { booleanAttribute, Component, computed, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

export type UiPriorityLevel = 'critical' | 'high' | 'medium' | 'low' | 'none';

export const UI_PRIORITY_LEVELS: readonly UiPriorityLevel[] = [
  'critical',
  'high',
  'medium',
  'low',
  'none',
];

const PRIORITY_LABELS: Record<UiPriorityLevel, string> = {
  critical: 'ui.priority.critical',
  high: 'ui.priority.high',
  medium: 'ui.priority.medium',
  low: 'ui.priority.low',
  none: 'ui.priority.none',
};

@Component({
  selector: 'ui-priority',
  imports: [TranslocoPipe],
  host: { '[class]': "'is-' + level()", '[class.is-symbol-only]': 'symbolOnly()' },
  template: `
    <svg viewBox="0 0 24 24" aria-hidden="true">
      @switch (level()) {
        @case ('critical') {
          <path class="shape" d="M7.4 1.5h9.2l5.9 5.9v9.2l-5.9 5.9H7.4l-5.9-5.9V7.4l5.9-5.9Z" />
          <rect class="mark" x="10.9" y="6.6" width="2.2" height="7.6" rx="1.1" />
          <circle class="mark" cx="12" cy="17" r="1.3" />
        }
        @case ('high') {
          <path
            class="shape"
            d="M10.3 3.6a2 2 0 0 1 3.4 0l8 13.9a2 2 0 0 1-1.7 3H4a2 2 0 0 1-1.7-3l8-13.9Z"
          />
          <rect class="mark" x="10.9" y="8.4" width="2.2" height="6.6" rx="1.1" />
          <circle class="mark" cx="12" cy="17.6" r="1.25" />
        }
        @case ('medium') {
          <path class="shape" d="M12 1.2 22.8 12 12 22.8 1.2 12 12 1.2Z" />
          <rect class="mark" x="10.9" y="6.8" width="2.2" height="7" rx="1.1" />
          <circle class="mark" cx="12" cy="16.6" r="1.25" />
        }
        @case ('low') {
          <circle class="line" cx="12" cy="12" r="8.6" />
          <path class="line" d="M12 7.6v8.4M8.4 12.6l3.6 3.6 3.6-3.6" />
        }
        @default {
          <circle class="line" cx="12" cy="12" r="8.6" />
          <path class="line" d="M8 12h8" />
        }
      }
    </svg>
    <span [class.sr-only]="symbolOnly()">{{ label() | transloco }}</span>
  `,
  styles: `
    :host {
      --c: var(--low);
      display: inline-flex;
      align-items: center;
      gap: 6px;
      color: var(--c);
      font-weight: 500;
      white-space: nowrap;
    }
    :host(.is-critical) {
      --c: var(--critical);
    }
    :host(.is-high) {
      --c: var(--high);
    }
    :host(.is-medium) {
      --c: var(--medium);
    }
    :host(.is-none) {
      font-weight: 400;
    }
    svg {
      width: 1.1em;
      height: 1.1em;
      flex: none;
    }
    .shape {
      fill: currentColor;
    }
    .mark {
      fill: var(--surface);
    }
    .line {
      fill: none;
      stroke: currentColor;
      stroke-width: 2.2;
      stroke-linecap: round;
      stroke-linejoin: round;
    }
  `,
})
export class UiPriority {
  readonly level = input.required<UiPriorityLevel>();
  readonly symbolOnly = input(false, { transform: booleanAttribute });
  protected readonly label = computed(() => PRIORITY_LABELS[this.level()]);
}
