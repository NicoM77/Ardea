import {
  booleanAttribute,
  Component,
  computed,
  DestroyRef,
  inject,
  input,
  numberAttribute,
  OnInit,
  signal,
} from '@angular/core';

export type UiLoaderSize = 'page' | 'inline' | 'button';

const PIXELS: Record<UiLoaderSize, number> = { page: 96, inline: 20, button: 16 };

/** The heron's lines (docs/design/brand), each normalised to a path length of 100. */
const LINES = [
  {
    name: 'back',
    d: 'M6 25 L27 16.5 C29 11.5 36 10.5 39 14.5 C42 18.5 40 23.5 36.5 27.5 C33 31.5 33 36 37 40 C42 45 46 50 46 58',
  },
  {
    name: 'front',
    d: 'M6 25 L28 22 C29.5 26 27.5 29.5 26 32.5 C24 37 24.5 42 28.5 46 C31.5 49 32 53 32 58',
  },
  { name: 'crest', d: 'M39 14.5 L47 12' },
] as const;

/**
 * The loader (DESIGN.md, Components, Loader; variant Draw from docs/design/loader): the heron draws
 * itself from the beak to the feet, the front line 90ms behind, then crest and eye appear; it holds
 * and the line leaves the way it came, 2.4s per cycle. Ink only.
 *
 * - Sizes: 96px for a page, 20px inline, 16px in a button. From 24px down the line is 4.6 and the
 *   eye is left out, as on the favicon.
 * - It appears only after `delay` (300ms), so a quick answer never flashes it.
 * - `done` hands over: the loop fades out in 150ms and the mark draws once in 700ms, the eye last.
 * - Reduced motion: the finished mark fades slowly between full and 35% opacity, without drawing.
 * - The host is a `role="status"` region with the label as a word beside the mark (or for screen
 *   readers only, in a button); the container that waits sets `aria-busy`.
 * - `frame` (0 to 1) draws one still frame of the loop, for the component pages and image comparison.
 */
@Component({
  selector: 'ui-loader',
  host: {
    '[attr.role]': "label() ? 'status' : null",
    '[class]': "'is-' + size()",
    '[class.is-visible]': 'visible()',
    '[class.is-done]': 'done()',
    '[class.is-still]': 'frame() !== null',
  },
  template: `
    @if (visible()) {
      <svg
        class="heron"
        viewBox="-1.5 6.25 56 56"
        [attr.width]="pixels()"
        [attr.height]="pixels()"
        aria-hidden="true"
        focusable="false"
      >
        <g class="loop" [attr.stroke-width]="strokeWidth()">
          @for (line of lines; track line.name) {
            <path
              [class]="line.name"
              [attr.d]="line.d"
              pathLength="100"
              [style.stroke-dashoffset]="stillOffset(line.name)"
            />
          }
          @if (!small()) {
            <circle class="eye" cx="32" cy="17" r="1.9" [style.opacity]="stillEye()" />
          }
        </g>
        <g class="final" [attr.stroke-width]="strokeWidth()">
          @for (line of lines; track line.name) {
            <path [attr.d]="line.d" pathLength="100" />
          }
          @if (!small()) {
            <circle class="eye" cx="32" cy="17" r="1.9" />
          }
        </g>
      </svg>
      @if (label()) {
        <span class="label" [class.sr-only]="size() === 'button'">{{ label() }}</span>
      }
    }
  `,
  styles: `
    :host {
      display: inline-flex;
      align-items: center;
      gap: var(--space-sm);
      color: var(--muted);
    }
    :host(.is-page) {
      flex-direction: column;
      justify-content: center;
      gap: 14px;
      color: var(--faint);
      font-size: var(--fs-sub);
      line-height: var(--lh-small);
    }
    :host(.is-button) {
      gap: 0;
    }
    .heron {
      display: block;
      flex: none;
      overflow: visible;
    }
    g {
      fill: none;
      stroke: var(--text);
      stroke-linecap: round;
      stroke-linejoin: round;
    }
    :host(.is-button) g {
      stroke: currentColor;
    }
    .eye {
      stroke: none;
      fill: var(--text);
    }
    :host(.is-button) .eye {
      fill: currentColor;
    }
    .final {
      opacity: 0;
    }

    /* The loop */
    .loop path {
      stroke-dasharray: 100 100;
      animation: ui-loader-draw 2.4s var(--ease-in-out) infinite both;
    }
    .loop path.front {
      animation-delay: 90ms;
    }
    .loop path.crest {
      animation-name: ui-loader-crest;
    }
    .loop .eye {
      animation: ui-loader-eye 2.4s var(--ease-out) infinite both;
    }
    :host(.is-still) .loop path,
    :host(.is-still) .loop .eye {
      animation: none;
    }

    /* Done: the loop gives way to the mark, drawn once from the beak */
    :host(.is-done) .loop {
      opacity: 0;
      transition: opacity 150ms ease-out;
    }
    :host(.is-done) .final {
      opacity: 1;
    }
    :host(.is-done) .final path {
      stroke-dasharray: 100 100;
      animation: ui-loader-settle 700ms var(--ease-out) both;
    }
    :host(.is-done) .final .eye {
      animation: ui-loader-fade-in 220ms var(--ease-out) 420ms both;
    }

    @keyframes ui-loader-draw {
      0% {
        stroke-dashoffset: 100;
      }
      44%,
      74% {
        stroke-dashoffset: 0;
      }
      100% {
        stroke-dashoffset: -100;
      }
    }
    @keyframes ui-loader-crest {
      0%,
      36% {
        stroke-dashoffset: 100;
      }
      52%,
      74% {
        stroke-dashoffset: 0;
      }
      90%,
      100% {
        stroke-dashoffset: -100;
      }
    }
    @keyframes ui-loader-eye {
      0%,
      42% {
        opacity: 0;
      }
      50%,
      76% {
        opacity: 1;
      }
      86%,
      100% {
        opacity: 0;
      }
    }
    @keyframes ui-loader-settle {
      from {
        stroke-dashoffset: 100;
      }
      to {
        stroke-dashoffset: 0;
      }
    }
    @keyframes ui-loader-fade-in {
      from {
        opacity: 0;
      }
    }
    @keyframes ui-loader-quiet {
      to {
        opacity: 0.35;
      }
    }

    /* Reduced motion: no drawing, the finished mark fades slowly between two tones */
    @media (prefers-reduced-motion: reduce) {
      :host(:not(.is-still)) .loop {
        display: none;
      }
      :host(:not(.is-still)) .final {
        opacity: 1;
      }
      :host(:not(.is-still)) .final path,
      :host(:not(.is-still)) .final .eye {
        animation: none;
      }
      :host(:not(.is-still):not(.is-done)) .final {
        animation: ui-loader-quiet 1.6s ease-in-out infinite alternate;
      }
    }
  `,
})
export class UiLoader implements OnInit {
  /**
   * What is loading, as a word or short sentence, already translated. Without one the loader is
   * decorative, for a control that announces its own busy state (`aria-busy` on a button).
   */
  readonly label = input<string | null>(null);
  readonly size = input<UiLoaderSize>('inline');
  /** Milliseconds of waiting before the loader shows. */
  readonly delay = input(300, { transform: numberAttribute });
  readonly done = input(false, { transform: booleanAttribute });
  /** A still frame of the loop, 0 (nothing drawn) to 1 (the whole mark). */
  readonly frame = input<number | null>(null);

  protected readonly lines = LINES;
  protected readonly visible = signal(false);
  protected readonly pixels = computed(() => PIXELS[this.size()]);
  protected readonly small = computed(() => this.pixels() <= 24);
  protected readonly strokeWidth = computed(() => (this.small() ? 4.6 : 3.8));

  private readonly destroyRef = inject(DestroyRef);

  ngOnInit(): void {
    const delay = this.delay();
    if (delay <= 0) {
      this.visible.set(true);
      return;
    }
    const timer = setTimeout(() => this.visible.set(true), delay);
    this.destroyRef.onDestroy(() => clearTimeout(timer));
  }

  /** The neck lines draw over the whole frame range, the crest over its last fifth. */
  protected stillOffset(line: string): number | null {
    const frame = this.frame();
    if (frame === null) return null;
    const progress = line === 'crest' ? (frame - 0.8) / 0.2 : frame;
    return 100 * (1 - Math.min(1, Math.max(0, progress)));
  }

  protected stillEye(): number | null {
    const frame = this.frame();
    return frame === null ? null : frame >= 1 ? 1 : 0;
  }
}
