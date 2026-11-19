import { Component, input, model } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

let nextGroup = 0;

/** An option: its value and the translation keys of its word and its one sentence. */
export interface ChoiceOption {
  value: string;
  label: string;
  hint?: string;
}

/**
 * One question with a few options, each with one sentence (master plan 11.6): native radio
 * buttons in a fieldset, drawn as cards. The legend is the question.
 */
@Component({
  selector: 'ardea-choice-group',
  imports: [TranslocoPipe],
  template: `
    <fieldset>
      <legend>
        {{ legend() | transloco }}
        @if (hint()) {
          <small>{{ hint() | transloco }}</small>
        }
      </legend>
      <div class="choices">
        @for (option of options(); track option.value) {
          <label class="choice" [class.is-checked]="option.value === value()">
            <input
              type="radio"
              [name]="name"
              [value]="option.value"
              [checked]="option.value === value()"
              (change)="value.set(option.value)"
            />
            <b>{{ option.label | transloco }}</b>
            @if (option.hint) {
              <small>{{ option.hint | transloco }}</small>
            }
          </label>
        }
      </div>
    </fieldset>
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }
    fieldset {
      display: grid;
      gap: 10px;
      min-width: 0;
      margin: 0;
      padding: 0;
      border: 0;
    }
    legend {
      float: left;
      width: 100%;
      padding: 0;
      font-weight: 500;
    }
    legend small,
    .choice small {
      display: block;
      color: var(--muted);
      font-size: var(--fs-sub);
      font-weight: 400;
      line-height: var(--lh-sub);
    }
    .choices {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
      gap: var(--space-sm);
    }
    .choice {
      position: relative;
      display: grid;
      align-content: start;
      gap: 2px;
      min-height: 64px;
      padding: 10px 12px 10px 36px;
      border: 1px solid var(--control);
      border-radius: var(--radius-card);
      background: var(--surface);
      cursor: pointer;
      transition:
        border-color var(--t-hover) ease,
        transform var(--t-press) var(--ease-out);
    }
    .choice:active {
      transform: scale(0.99);
    }
    .choice input {
      position: absolute;
      left: 12px;
      top: 12px;
      width: 16px;
      height: 16px;
      margin: 0;
      accent-color: var(--text);
    }
    .choice b {
      font-weight: 500;
    }
    .choice.is-checked {
      border-color: var(--text);
      box-shadow: inset 0 0 0 1px var(--text);
    }
    .choice:has(input:focus-visible) {
      outline: var(--focus-ring);
      outline-offset: var(--focus-offset);
    }
    @media (max-width: 640px) {
      .choices {
        grid-template-columns: minmax(0, 1fr);
      }
      .choice {
        min-height: 0;
      }
    }
    @media (hover: hover) and (pointer: fine) {
      .choice:hover {
        border-color: var(--text);
      }
    }
  `,
})
export class ChoiceGroup {
  readonly legend = input.required<string>();
  readonly hint = input<string | null>(null);
  readonly options = input.required<readonly ChoiceOption[]>();
  readonly value = model<string | null>(null);
  protected readonly name = `choice-${++nextGroup}`;
}
