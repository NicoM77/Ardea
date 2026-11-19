import { Component, inject, Injectable, input, output, signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

/** A confirmation: the sentence, and what Undo does when there is a way back. */
export interface UiToastMessage {
  /** Already translated. */
  text: string;
  undo?: () => void;
}

/**
 * The confirmation toast (DESIGN.md, Feedback): a change is confirmed by a dark toast at the
 * bottom with Undo, never by a dialog. Presentational; `UiToaster` and `ui-toast-host` show it.
 */
@Component({
  selector: 'ui-toast',
  imports: [TranslocoPipe],
  template: `
    <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m5 12.5 4.5 4.5L19 7.5" /></svg>
    <span class="text">{{ text() }}</span>
    @if (undoable()) {
      <button type="button" class="undo" (click)="undo.emit()">
        {{ 'ui.toast.undo' | transloco }}
      </button>
    }
  `,
  styles: `
    :host {
      display: inline-flex;
      align-items: center;
      gap: 10px;
      max-width: min(560px, calc(100vw - 32px));
      min-height: 44px;
      padding: 0 6px 0 14px;
      border-radius: var(--radius-toast);
      background: var(--primary);
      color: var(--primary-ink);
      box-shadow: var(--shadow-toast);
      font-weight: 500;
    }
    svg {
      width: 16px;
      height: 16px;
      flex: none;
      fill: none;
      stroke: var(--toast-check);
      stroke-width: 2.4;
      stroke-linecap: round;
      stroke-linejoin: round;
    }
    .text {
      min-width: 0;
      padding: 10px 8px 10px 0;
      overflow-wrap: anywhere;
    }
    .undo {
      flex: none;
      height: 32px;
      padding: 0 12px;
      border: 0;
      border-radius: var(--radius-control);
      background: var(--on-dark-fill);
      color: var(--primary-ink);
      font-weight: 500;
      cursor: pointer;
    }
    .undo:focus-visible {
      outline: 2px solid var(--primary-ink);
      outline-offset: 2px;
    }
    @media (hover: hover) and (pointer: fine) {
      .undo:hover {
        background: var(--on-dark-fill-hover);
      }
    }
  `,
})
export class UiToast {
  readonly text = input.required<string>();
  readonly undoable = input(false);
  readonly undo = output();
}

/**
 * Shows one confirmation at a time. A new one replaces the last; it leaves after a while unless
 * the pointer or the focus rests on it, so Undo stays in reach.
 */
@Injectable({ providedIn: 'root' })
export class UiToaster {
  /** How long a confirmation stays; long enough to read it and reach Undo. */
  static readonly duration = 6000;

  private readonly shown = signal<(UiToastMessage & { id: number }) | null>(null);
  readonly current = this.shown.asReadonly();
  private timer: ReturnType<typeof setTimeout> | undefined;
  private next = 0;
  private held = false;

  show(message: UiToastMessage): void {
    this.shown.set({ ...message, id: ++this.next });
    this.arm();
  }

  dismiss(): void {
    clearTimeout(this.timer);
    this.shown.set(null);
  }

  /** Runs the toast's Undo once and removes it. */
  undo(): void {
    const message = this.shown();
    this.dismiss();
    message?.undo?.();
  }

  /** While the pointer or the focus is on the toast, it stays. */
  hold(held: boolean): void {
    this.held = held;
    if (held) clearTimeout(this.timer);
    else this.arm();
  }

  private arm(): void {
    clearTimeout(this.timer);
    if (this.held || !this.shown()) return;
    this.timer = setTimeout(() => this.shown.set(null), UiToaster.duration);
  }
}

/**
 * Where confirmations appear: fixed at the bottom, above a phone's action bar. The region is
 * always in the page and polite, so screen readers hear each new sentence once.
 */
@Component({
  selector: 'ui-toast-host',
  imports: [UiToast],
  host: { role: 'status', 'aria-live': 'polite' },
  template: `
    @if (toaster.current(); as message) {
      @for (shown of [message]; track shown.id) {
        <ui-toast
          [text]="shown.text"
          [undoable]="!!shown.undo"
          (undo)="toaster.undo()"
          (pointerenter)="toaster.hold(true)"
          (pointerleave)="toaster.hold(false)"
          (focusin)="toaster.hold(true)"
          (focusout)="toaster.hold(false)"
        />
      }
    }
  `,
  styles: `
    :host {
      position: fixed;
      left: 0;
      right: 0;
      bottom: calc(var(--space-lg) + var(--toast-lift, 0px));
      z-index: 25;
      display: flex;
      justify-content: center;
      pointer-events: none;
    }
    ui-toast {
      pointer-events: auto;
      animation: ui-toast-in var(--t-ui) var(--ease-out) both;
    }
    /* A page with a fixed action bar at phone width (an action) lifts the toast above it. */
    @media (max-width: 640px) {
      :host-context(body:has(.ui-actionbar)) {
        --toast-lift: 64px;
      }
    }
    @keyframes ui-toast-in {
      from {
        opacity: 0;
        transform: translateY(8px) scale(0.98);
      }
    }
    @media (prefers-reduced-motion: reduce) {
      ui-toast {
        animation-name: ui-toast-fade;
      }
      @keyframes ui-toast-fade {
        from {
          opacity: 0;
        }
      }
    }
  `,
})
export class UiToastHost {
  protected readonly toaster = inject(UiToaster);
}
