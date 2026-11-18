import { Component, computed, input } from '@angular/core';
import { encode } from 'uqr';
import { qrPath } from './qr-path';

/**
 * A QR code drawn as one SVG path, in Text on the sheet's white, with the quiet zone of four
 * modules the standard asks for. Rendered locally: the secret never leaves the browser.
 * The code is an image with a label; the same content must also be offered as text nearby.
 */
@Component({
  selector: 'ui-qr-code',
  template: `
    <svg
      role="img"
      [attr.aria-label]="label()"
      [attr.viewBox]="'0 0 ' + qr().size + ' ' + qr().size"
      shape-rendering="crispEdges"
    >
      <rect class="paper" [attr.width]="qr().size" [attr.height]="qr().size" />
      <path class="ink" [attr.d]="path()" />
    </svg>
  `,
  styles: `
    :host {
      display: block;
      width: 176px;
      max-width: 100%;
      padding: var(--space-xs);
      border: 1px solid var(--border);
      border-radius: var(--radius-card);
      background: var(--surface);
    }
    svg {
      width: 100%;
      height: auto;
      aspect-ratio: 1;
    }
    .paper {
      fill: var(--surface);
    }
    .ink {
      fill: var(--text);
    }
  `,
})
export class UiQrCode {
  readonly value = input.required<string>();
  /** What the code holds, for people who cannot see it. Already translated. */
  readonly label = input.required<string>();

  protected readonly qr = computed(() => encode(this.value(), { ecc: 'M', border: 4 }));
  protected readonly path = computed(() => qrPath(this.qr().data));
}
