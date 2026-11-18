import { Component, input, signal } from '@angular/core';
import { LucideEye, LucideEyeOff } from '@lucide/angular';
import { TranslocoPipe } from '@jsverse/transloco';

/**
 * Shows or hides the password in the input it points to. Pasting from a password manager stays
 * allowed (WCAG 2.2, accessible authentication); seeing what was typed replaces a second field.
 *
 * Usage: `<input uiInput type="password" #pw /> <ui-password-toggle [control]="pw" />`
 */
@Component({
  selector: 'ui-password-toggle',
  imports: [LucideEye, LucideEyeOff, TranslocoPipe],
  template: `
    <button
      type="button"
      [attr.aria-pressed]="visible()"
      [attr.aria-controls]="control().id || null"
      [attr.aria-label]="(visible() ? 'ui.password.hide' : 'ui.password.show') | transloco"
      (click)="toggle()"
    >
      @if (visible()) {
        <svg lucideEyeOff aria-hidden="true"></svg>
      } @else {
        <svg lucideEye aria-hidden="true"></svg>
      }
    </button>
  `,
  styles: `
    :host {
      position: absolute;
      top: 0;
      right: 0;
      bottom: 0;
      display: grid;
      place-items: center;
      width: var(--control-height);
    }
    button {
      display: grid;
      place-items: center;
      width: calc(var(--control-height) - 8px);
      height: calc(var(--control-height) - 8px);
      padding: 0;
      border: 0;
      border-radius: var(--radius-key);
      background: transparent;
      color: var(--muted);
      cursor: pointer;
      transition:
        background-color var(--t-hover) ease,
        color var(--t-hover) ease;
    }
    button:focus-visible {
      outline: var(--focus-ring);
      outline-offset: 0;
    }
    @media (hover: hover) and (pointer: fine) {
      button:hover {
        background: var(--raised);
        color: var(--text);
      }
    }
  `,
})
export class UiPasswordToggle {
  readonly control = input.required<HTMLInputElement>();
  protected readonly visible = signal(false);

  protected toggle(): void {
    const next = !this.visible();
    this.visible.set(next);
    this.control().type = next ? 'text' : 'password';
  }
}
