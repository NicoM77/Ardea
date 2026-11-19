import { Component, input } from '@angular/core';

export type NotifyIconName =
  'mail' | 'smartphone' | 'webhook' | 'trash' | 'share' | 'square-plus' | 'user-check' | 'inbox';

/**
 * The few line icons only the notification pages use, drawn like the Lucide set the app is configured with
 * (16px, 1.6 stroke; paths from Lucide, ISC). Inline here because every icon imported from `@lucide/angular`
 * lands in the initial bundle, which is already over its budget; these stay in the pages' lazy chunks.
 */
@Component({
  selector: 'ardea-notify-icon',
  host: { 'aria-hidden': 'true' },
  template: `
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      stroke-width="1.6"
      stroke-linecap="round"
      stroke-linejoin="round"
    >
      @switch (name()) {
        @case ('mail') {
          <rect width="20" height="16" x="2" y="4" rx="2" />
          <path d="m22 7-8.97 5.7a1.94 1.94 0 0 1-2.06 0L2 7" />
        }
        @case ('smartphone') {
          <rect width="14" height="20" x="5" y="2" rx="2" ry="2" />
          <path d="M12 18h.01" />
        }
        @case ('webhook') {
          <path d="M18 16.98h-5.99c-1.1 0-1.95.94-2.48 1.9A4 4 0 0 1 2 17c.01-.7.2-1.4.57-2" />
          <path d="m6 17 3.13-5.78c.53-.97.1-2.18-.5-3.1a4 4 0 1 1 6.89-4.06" />
          <path d="m12 6 3.13 5.73C15.66 12.7 16.9 13 18 13a4 4 0 0 1 0 8" />
        }
        @case ('trash') {
          <path d="M3 6h18" />
          <path d="M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6" />
          <path d="M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2" />
          <path d="M10 11v6M14 11v6" />
        }
        @case ('share') {
          <path d="M4 12v8a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-8" />
          <path d="m16 6-4-4-4 4M12 2v13" />
        }
        @case ('square-plus') {
          <rect width="18" height="18" x="3" y="3" rx="2" />
          <path d="M8 12h8M12 8v8" />
        }
        @case ('user-check') {
          <path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2" />
          <circle cx="9" cy="7" r="4" />
          <path d="m16 11 2 2 4-4" />
        }
        @case ('inbox') {
          <path d="M22 12h-6l-2 3h-4l-2-3H2" />
          <path
            d="M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z"
          />
        }
      }
    </svg>
  `,
  styles: `
    :host {
      display: inline-flex;
      flex: none;
      width: 16px;
      height: 16px;
    }
    svg {
      width: 100%;
      height: 100%;
    }
  `,
})
export class NotifyIcon {
  readonly name = input.required<NotifyIconName>();
}
