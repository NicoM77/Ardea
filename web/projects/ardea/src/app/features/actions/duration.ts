import { Component, computed, DestroyRef, inject, input, signal, Signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { durationParts, UNIT_KEYS } from './action-format';

/**
 * The current time as a signal, read again every `everyMs` while the page is open: deadlines count
 * down without a request. Call in an injection context.
 */
export function injectNow(everyMs = 60_000): Signal<Date> {
  const now = signal(new Date());
  const timer = setInterval(() => now.set(new Date()), everyMs);
  inject(DestroyRef).onDestroy(() => clearInterval(timer));
  return now.asReadonly();
}

/**
 * A span such as «1 d 13 h» (DESIGN.md: units are quieter than figures): the numbers in Text, the
 * units in Muted.
 */
@Component({
  selector: 'ardea-duration',
  imports: [TranslocoPipe],
  template: `
    @for (part of shown(); track part.unit) {
      <b>{{ part.value }}</b><small>{{ unitKeys[part.unit] | transloco }}</small>
    }
  `,
  styles: `
    :host {
      white-space: nowrap;
    }
    b {
      font-weight: inherit;
    }
    small {
      margin: 0 0.4em 0 0.1em;
      color: var(--muted);
      font-size: 0.8em;
      font-weight: 400;
    }
    small:last-child {
      margin-right: 0;
    }
  `,
})
export class Duration {
  readonly ms = input.required<number>();
  readonly parts = input(2);
  readonly seconds = input(false);
  protected readonly unitKeys = UNIT_KEYS;
  protected readonly shown = computed(() => durationParts(this.ms(), this.parts(), this.seconds()));
}
