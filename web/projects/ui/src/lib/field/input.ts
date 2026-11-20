import { booleanAttribute, Directive, inject, input } from '@angular/core';
import { UiField } from './field';

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
  readonly mono = input(false, { transform: booleanAttribute });
  readonly id = input<string | null>(null);
}
