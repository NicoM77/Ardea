import { booleanAttribute, Directive, inject, input } from '@angular/core';
import { UiField } from './field';

/**
 * Marks a native control as a design-system field. Inside `ui-field` it takes the field's id and
 * descriptions; on its own it only takes the look. The look lives in `styles/controls.css`
 * because a native input cannot carry component styles.
 */
@Directive({
  selector: 'input[uiInput], textarea[uiInput], select[uiInput]',
  host: {
    class: 'ui-input',
    '[class.is-mono]': 'mono()',
    '[attr.id]': 'field?.controlId ?? id()',
    '[attr.aria-describedby]': 'field?.describedBy() ?? null',
    '[attr.aria-invalid]': 'field?.invalid() ? "true" : null',
  },
})
export class UiInput {
  protected readonly field = inject(UiField, { optional: true });
  /** IBM Plex Mono, for strings people compare and copy: codes, keys, identifiers. */
  readonly mono = input(false, { transform: booleanAttribute });
  /** Outside a `ui-field` the id is the author's; inside, the field owns it. */
  readonly id = input<string | null>(null);
}
