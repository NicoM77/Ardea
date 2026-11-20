import { booleanAttribute, Component, computed, input } from '@angular/core';
import { LucideCircleExclamationPoint } from '@lucide/angular';
import { TranslocoPipe } from '@jsverse/transloco';

let nextId = 0;

@Component({
  selector: 'ui-field',
  imports: [LucideCircleExclamationPoint, TranslocoPipe],
  host: {
    class: 'ui-field',
    '[class.is-invalid]': '!!error()',
  },
  template: `
    <label class="label" [attr.for]="controlId">
      {{ label() }}
      @if (optional()) {
        <span class="optional">{{ 'ui.field.optional' | transloco }}</span>
      }
    </label>
    @if (hint()) {
      <p class="hint" [id]="hintId">{{ hint() }}</p>
    }
    <div class="control">
      <ng-content />
    </div>
    @if (error()) {
      <p class="error" [id]="errorId">
        <svg lucideCircleExclamationPoint aria-hidden="true"></svg>
        <span>{{ error() }}</span>
      </p>
    }
  `,
  styles: `
    :host {
      display: grid;
      gap: 6px;
      min-width: 0;
    }
    .label {
      font-weight: 500;
      justify-self: start;
    }
    .optional {
      margin-left: 6px;
      color: var(--muted);
      font-weight: 400;
    }
    .hint {
      margin-top: -2px;
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .control {
      position: relative;
      display: flex;
      min-width: 0;
    }
    .error {
      display: flex;
      align-items: flex-start;
      gap: 6px;
      color: var(--text);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
      font-weight: 500;
    }
    .error svg {
      width: 14px;
      height: 14px;
      margin-top: 2px;
    }
  `,
})
export class UiField {
  readonly label = input.required<string>();
  readonly hint = input<string | null>(null);
  readonly error = input<string | null | undefined>(null);
  readonly optional = input(false, { transform: booleanAttribute });

  private readonly id = `ui-field-${nextId++}`;
  readonly controlId = `${this.id}-control`;
  readonly hintId = `${this.id}-hint`;
  readonly errorId = `${this.id}-error`;

  readonly describedBy = computed(() => {
    const ids = [this.hint() ? this.hintId : null, this.error() ? this.errorId : null].filter(
      (id): id is string => id !== null,
    );
    return ids.length ? ids.join(' ') : null;
  });
  readonly invalid = computed(() => !!this.error());
}
