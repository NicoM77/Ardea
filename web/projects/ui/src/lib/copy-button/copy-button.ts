import { Component, DestroyRef, inject, input, signal } from '@angular/core';
import { LucideCheck, LucideCopy } from '@lucide/angular';
import { TranslocoPipe } from '@jsverse/transloco';
import { UiButton } from '../button/button';

@Component({
  selector: 'ui-copy-button',
  imports: [UiButton, LucideCopy, LucideCheck, TranslocoPipe],
  template: `
    <button uiButton type="button" [variant]="variant()" (click)="copy()">
      @if (copied()) {
        <svg lucideCheck aria-hidden="true"></svg>
        {{ 'ui.copy.done' | transloco }}
      } @else {
        <svg lucideCopy aria-hidden="true"></svg>
        {{ label() }}
      }
    </button>
    <span class="sr-only" role="status">{{ copied() ? ('ui.copy.done' | transloco) : '' }}</span>
  `,
  styles: `
    :host {
      display: inline-flex;
    }
  `,
})
export class UiCopyButton {
  readonly text = input.required<string>();
  readonly label = input.required<string>();
  readonly variant = input<'default' | 'ghost'>('default');

  protected readonly copied = signal(false);
  private timer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.timer));
  }

  protected async copy(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.text());
    } catch {
      return;
    }
    this.copied.set(true);
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.copied.set(false), 2000);
  }
}
