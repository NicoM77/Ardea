import { Component, inject, Injectable, input, output, signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

export interface UiToastMessage {
  text: string;
  undo?: () => void;
}

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

@Injectable({ providedIn: 'root' })
export class UiToaster {
  static readonly duration = 6000;
  static readonly exit = 150;

  private readonly shown = signal<(UiToastMessage & { id: number }) | null>(null);
  private readonly drawn = signal<(UiToastMessage & { id: number }) | null>(null);
  private readonly fading = signal(false);
  readonly current = this.shown.asReadonly();
  readonly rendered = this.drawn.asReadonly();
  readonly leaving = this.fading.asReadonly();
  private timer: ReturnType<typeof setTimeout> | undefined;
  private exitTimer: ReturnType<typeof setTimeout> | undefined;
  private next = 0;
  private held = false;

  show(message: UiToastMessage): void {
    clearTimeout(this.exitTimer);
    const toast = { ...message, id: ++this.next };
    this.fading.set(false);
    this.shown.set(toast);
    this.drawn.set(toast);
    this.arm();
  }

  dismiss(): void {
    clearTimeout(this.timer);
    this.leave();
  }

  undo(): void {
    const message = this.shown();
    this.dismiss();
    message?.undo?.();
  }

  hold(held: boolean): void {
    this.held = held;
    if (held) clearTimeout(this.timer);
    else this.arm();
  }

  private leave(): void {
    if (!this.shown()) return;
    this.shown.set(null);
    this.fading.set(true);
    clearTimeout(this.exitTimer);
    this.exitTimer = setTimeout(() => {
      this.drawn.set(null);
      this.fading.set(false);
    }, UiToaster.exit);
  }

  private arm(): void {
    clearTimeout(this.timer);
    if (this.held || !this.shown()) return;
    this.timer = setTimeout(() => this.leave(), UiToaster.duration);
  }
}

@Component({
  selector: 'ui-toast-host',
  imports: [UiToast],
  host: { role: 'status', 'aria-live': 'polite' },
  template: `
    @if (toaster.rendered(); as message) {
      @for (shown of [message]; track shown.id) {
        <ui-toast
          [text]="shown.text"
          [undoable]="!!shown.undo"
          [class.leaving]="toaster.leaving()"
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
    ui-toast.leaving {
      pointer-events: none;
      animation: ui-toast-out var(--t-hover) ease both;
    }
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
    @keyframes ui-toast-out {
      to {
        opacity: 0;
        transform: translateY(4px);
      }
    }
    @media (prefers-reduced-motion: reduce) {
      ui-toast {
        animation-name: ui-toast-fade;
      }
      ui-toast.leaving {
        animation-name: ui-toast-fade-out;
      }
      @keyframes ui-toast-fade-out {
        to {
          opacity: 0;
        }
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
