import {
  Component,
  ElementRef,
  input,
  output,
  signal,
  viewChild,
  afterNextRender,
} from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { UiButton, UiInput } from 'ui';

@Component({
  selector: 'ardea-reason-form',
  imports: [TranslocoPipe, UiButton, UiInput],
  template: `
    <form novalidate (submit)="submit($event)">
      <label class="field" [for]="id">{{ label() }}</label>
      <textarea
        uiInput
        #text
        [id]="id"
        rows="3"
        maxlength="2000"
        [attr.aria-invalid]="missing() || null"
        [attr.aria-describedby]="missing() ? id + '-error' : null"
        [placeholder]="'actionDetail.reason.placeholder' | transloco"
        (input)="missing.set(false)"
      ></textarea>
      @if (missing()) {
        <p class="error" [id]="id + '-error'">{{ 'actionDetail.reason.required' | transloco }}</p>
      }
      <div class="buttons">
        <button uiButton type="button" (click)="dismiss.emit()">
          {{ 'actionDetail.reason.cancel' | transloco }}
        </button>
        <button uiButton variant="primary" type="submit" [loading]="busy()">
          {{ submitLabel() }}
        </button>
      </div>
    </form>
  `,
  styles: `
    form {
      display: grid;
      gap: var(--space-sm);
    }
    .field {
      font-weight: 500;
    }
    .error {
      color: var(--text);
      font-size: var(--fs-sub);
    }
    .buttons {
      display: flex;
      flex-wrap: wrap;
      justify-content: flex-end;
      gap: var(--space-sm);
    }
  `,
})
export class ReasonForm {
  private static next = 0;
  protected readonly id = `reason-${ReasonForm.next++}`;

  readonly label = input.required<string>();
  readonly submitLabel = input.required<string>();
  readonly busy = input(false);
  readonly save = output<string>();
  readonly dismiss = output();

  protected readonly missing = signal(false);
  private readonly text = viewChild.required<ElementRef<HTMLTextAreaElement>>('text');

  constructor() {
    afterNextRender(() => {
      const field = this.text().nativeElement;
      field.scrollIntoView?.({ block: 'nearest' });
      field.focus();
    });
  }

  protected submit(event: Event): void {
    event.preventDefault();
    const value = this.text().nativeElement.value.trim();
    if (!value) {
      this.missing.set(true);
      this.text().nativeElement.focus();
      return;
    }
    this.save.emit(value);
  }
}
