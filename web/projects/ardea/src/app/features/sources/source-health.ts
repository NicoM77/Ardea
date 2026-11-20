import { Component, computed, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import {
  LucideCircleCheck,
  LucideCircleDashed,
  LucideCirclePause,
  LucideCircleX,
} from '@lucide/angular';
import { HEALTH_LABELS } from './source-format';

@Component({
  selector: 'ardea-source-health',
  imports: [TranslocoPipe, LucideCircleCheck, LucideCircleDashed, LucideCirclePause, LucideCircleX],
  host: { '[class]': "'is-' + health()" },
  template: `
    @switch (health()) {
      @case ('healthy') {
        <svg lucideCircleCheck aria-hidden="true"></svg>
      }
      @case ('degraded') {
        <svg lucideCircleDashed aria-hidden="true"></svg>
      }
      @case ('failing') {
        <svg lucideCircleX aria-hidden="true"></svg>
      }
      @default {
        <svg lucideCirclePause aria-hidden="true"></svg>
      }
    }
    <span>{{ label() | transloco }}</span>
  `,
  styles: `
    :host {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      font-weight: 500;
      white-space: nowrap;
    }
    svg {
      color: var(--muted);
    }
    :host(.is-healthy) svg {
      color: var(--positive);
    }
    :host(.is-failing) svg {
      color: var(--text);
    }
    :host(.is-disabled) {
      color: var(--muted);
      font-weight: 400;
    }
  `,
})
export class SourceHealth {
  readonly health = input.required<string>();
  protected readonly label = computed(
    () => HEALTH_LABELS[this.health()] ?? 'sources.health.healthy',
  );
}
