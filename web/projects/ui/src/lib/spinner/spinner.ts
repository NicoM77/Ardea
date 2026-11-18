import { Component } from '@angular/core';

/**
 * A small ring that turns while something is in progress. Decorative: the control that shows it
 * also carries `aria-busy` and keeps its label, so the state is never told by motion alone.
 */
@Component({
  selector: 'ui-spinner',
  host: { 'aria-hidden': 'true' },
  template: `
    <svg viewBox="0 0 16 16" width="16" height="16">
      <circle class="track" cx="8" cy="8" r="6" />
      <circle class="arc" cx="8" cy="8" r="6" />
    </svg>
  `,
  styles: `
    :host {
      display: inline-grid;
      place-items: center;
      width: 16px;
      height: 16px;
      flex: none;
    }
    svg {
      animation: ui-spin 800ms linear infinite;
    }
    circle {
      fill: none;
      stroke-width: 2;
    }
    .track {
      stroke: currentColor;
      opacity: 0.2;
    }
    .arc {
      stroke: currentColor;
      stroke-linecap: round;
      stroke-dasharray: 12 40;
    }
    @keyframes ui-spin {
      to {
        transform: rotate(360deg);
      }
    }
    @media (prefers-reduced-motion: reduce) {
      svg {
        animation-duration: 2400ms;
      }
    }
  `,
})
export class UiSpinner {}
