import {
  afterNextRender,
  Component,
  ElementRef,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { UiButton, UiInput, UiStatus } from 'ui';
import type { Outcome } from './action-format';
import { CLOSING_QUESTIONS } from './action-status';

/** The outcomes that close a reminder without remediating it; each needs a reason (master plan 17.5). */
export type OtherOutcome = Exclude<Outcome, 'remediated'>;

/** A closing chosen in the form: the outcome and its reason. */
export interface OtherClosing {
  outcome: OtherOutcome;
  note: string;
}

/**
 * «Other outcome» of a reminder (mockup reminder.html): Risk accepted or Not affected, each with a short
 * reason that stays in the history. «Mark as remediated» needs none and is not here. Opens with the focus
 * on the chosen outcome; Escape and Cancel close it.
 */
@Component({
  selector: 'ardea-outcome-form',
  imports: [TranslocoPipe, UiButton, UiInput, UiStatus],
  template: `
    <form novalidate (submit)="submit($event)" (keydown.escape)="escape($event)">
      <fieldset>
        <legend>{{ 'actionDetail.outcome.legend' | transloco }}</legend>
        <div class="choices">
          @for (o of outcomes; track o) {
            <label class="choice">
              <input
                type="radio"
                [name]="uid"
                [value]="o"
                [checked]="o === outcome()"
                (change)="outcome.set(o)"
                #radio
              />
              <ui-status [kind]="o === 'risk_accepted' ? 'accepted' : 'notAffected'" />
            </label>
          }
        </div>
      </fieldset>
      <label class="field" [for]="uid + '-reason'">{{ question() | transloco }}</label>
      <textarea
        uiInput
        #text
        [id]="uid + '-reason'"
        rows="3"
        maxlength="2000"
        [attr.aria-invalid]="missing() || null"
        [attr.aria-describedby]="missing() ? uid + '-error' : null"
        [placeholder]="'actionDetail.reason.placeholder' | transloco"
        (input)="missing.set(false)"
      ></textarea>
      @if (missing()) {
        <p class="error" [id]="uid + '-error'">{{ 'actionDetail.reason.required' | transloco }}</p>
      }
      <div class="buttons">
        <button uiButton type="button" (click)="dismiss.emit()">
          {{ 'actionDetail.reason.cancel' | transloco }}
        </button>
        <button uiButton variant="primary" type="submit" [loading]="busy()">
          {{ 'actionDetail.outcome.submit' | transloco }}
        </button>
      </div>
    </form>
  `,
  styles: `
    form {
      display: grid;
      gap: var(--space-md);
    }
    fieldset {
      margin: 0;
      padding: 0;
      border: 0;
      min-width: 0;
    }
    legend,
    .field {
      margin-bottom: var(--space-sm);
      font-weight: 500;
    }
    .field {
      margin-bottom: calc(-1 * var(--space-xs));
    }
    .choices {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-sm);
    }
    .choice {
      display: inline-flex;
      align-items: center;
      gap: var(--space-sm);
      min-height: var(--control-height);
      padding: 0 var(--space-md);
      border: 1px solid var(--control);
      border-radius: var(--radius-control);
      background: var(--surface);
      cursor: pointer;
    }
    .choice input {
      margin: 0;
      accent-color: var(--text);
    }
    .choice:has(input:checked) {
      border-color: var(--text);
    }
    .choice:has(input:focus-visible) {
      outline: var(--focus-ring);
      outline-offset: var(--focus-offset);
    }
    .error {
      font-size: var(--fs-sub);
      font-weight: 500;
    }
    .buttons {
      display: flex;
      flex-wrap: wrap;
      justify-content: flex-end;
      gap: var(--space-sm);
    }
    @media (max-width: 640px) {
      .choice {
        min-height: 44px;
      }
      .buttons button {
        flex: 1;
        min-height: 44px;
      }
    }
  `,
})
export class OutcomeForm {
  private static next = 0;
  protected readonly uid = `outcome-${OutcomeForm.next++}`;
  protected readonly outcomes: readonly OtherOutcome[] = ['risk_accepted', 'not_affected'];

  readonly busy = input(false);
  readonly save = output<OtherClosing>();
  readonly dismiss = output();

  protected readonly outcome = signal<OtherOutcome>('risk_accepted');
  protected readonly missing = signal(false);
  private readonly text = viewChild.required<ElementRef<HTMLTextAreaElement>>('text');
  private readonly first = viewChild.required<ElementRef<HTMLInputElement>>('radio');

  constructor() {
    afterNextRender(() => {
      const radio = this.first().nativeElement;
      radio.scrollIntoView?.({ block: 'nearest' });
      radio.focus();
    });
  }

  protected question(): string {
    return CLOSING_QUESTIONS[this.outcome()];
  }

  protected submit(event: Event): void {
    event.preventDefault();
    const note = this.text().nativeElement.value.trim();
    if (!note) {
      this.missing.set(true);
      this.text().nativeElement.focus();
      return;
    }
    this.save.emit({ outcome: this.outcome(), note });
  }

  protected escape(event: Event): void {
    event.preventDefault();
    this.dismiss.emit();
  }
}
