import { booleanAttribute, Component, DestroyRef, ElementRef, inject, input } from '@angular/core';
import { UiSpinner } from '../spinner/spinner';

export type UiButtonVariant = 'default' | 'primary' | 'ghost';

/**
 * Buttons (DESIGN.md, Components): 32px high (40px on phones), 6px corners, weight 500.
 * Primary is near-black and appears once per screen; Default has a Control edge; Ghost has none.
 *
 * While `loading` is set the button stays focusable, announces `aria-busy`, ignores clicks and
 * keeps its label beside the spinner, so a keyboard user never loses their place.
 */
@Component({
  // An attribute on the native element keeps its semantics, focus and form behaviour.
  // eslint-disable-next-line @angular-eslint/component-selector
  selector: 'button[uiButton], a[uiButton]',
  imports: [UiSpinner],
  host: {
    class: 'ui-button',
    '[class.is-primary]': "variant() === 'primary'",
    '[class.is-ghost]': "variant() === 'ghost'",
    '[class.is-icon]': 'iconOnly()',
    '[class.is-block]': 'block()',
    '[class.is-loading]': 'loading()',
    '[attr.aria-busy]': 'loading() || null',
    '[attr.aria-disabled]': 'loading() || null',
  },
  template: `
    @if (loading()) {
      <ui-spinner />
    }
    <ng-content />
  `,
  styles: `
    :host {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      gap: var(--space-sm);
      height: var(--control-height);
      padding: 0 var(--space-md);
      border: 1px solid var(--control);
      border-radius: var(--radius-control);
      background: var(--surface);
      color: var(--text);
      font-weight: 500;
      line-height: var(--lh-body);
      white-space: nowrap;
      text-decoration: none;
      cursor: pointer;
      user-select: none;
      transition:
        background-color var(--t-hover) ease,
        border-color var(--t-hover) ease,
        transform var(--t-press) var(--ease-out);
    }
    :host(:active:not(:disabled):not(.is-loading)) {
      transform: scale(0.97);
    }
    :host(:focus-visible),
    :host([data-ui-state='focus']) {
      outline: var(--focus-ring);
      outline-offset: var(--focus-offset);
    }
    :host(.is-primary) {
      background: var(--primary);
      border-color: var(--primary);
      color: var(--primary-ink);
    }
    :host(.is-ghost) {
      border-color: transparent;
      background: transparent;
    }
    :host(.is-icon) {
      width: var(--control-height);
      padding: 0;
    }
    :host(.is-block) {
      width: 100%;
    }
    :host(.is-loading) {
      cursor: progress;
    }
    :host(:disabled),
    :host(:disabled.is-primary) {
      border-color: var(--control);
      background: var(--raised);
      color: var(--faint);
      cursor: not-allowed;
    }
    :host([data-ui-state='hover']) {
      background: var(--raised);
    }
    :host(.is-primary[data-ui-state='hover']) {
      background: var(--primary-hover);
      border-color: var(--primary-hover);
    }
    @media (hover: hover) and (pointer: fine) {
      :host(:hover:not(:disabled)) {
        background: var(--raised);
      }
      :host(.is-primary:hover:not(:disabled)) {
        background: var(--primary-hover);
        border-color: var(--primary-hover);
      }
    }
  `,
})
export class UiButton {
  readonly variant = input<UiButtonVariant>('default');
  readonly loading = input(false, { transform: booleanAttribute });
  readonly iconOnly = input(false, { transform: booleanAttribute });
  readonly block = input(false, { transform: booleanAttribute });

  constructor() {
    // A capture listener on the element runs before the (click) handlers its parent template
    // binds, so a click while loading reaches neither them nor the form's submit.
    const element = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    const guard = (event: Event) => {
      if (!this.loading()) return;
      event.preventDefault();
      event.stopImmediatePropagation();
    };
    element.addEventListener('click', guard, { capture: true });
    inject(DestroyRef).onDestroy(() =>
      element.removeEventListener('click', guard, { capture: true }),
    );
  }
}
