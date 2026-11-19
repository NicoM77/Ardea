import { Component, inject, input, output } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { UiButton, UiCard, UiInput, UiPriority } from 'ui';
import type { HistoryLine } from './history-format';

/**
 * An action's history (master plan 8.2): newest first, each line with its event kind and id, so a
 * notification can be traced back to it. Members add notes for the team at the end.
 */
@Component({
  selector: 'ardea-action-history',
  imports: [TranslocoPipe, UiButton, UiCard, UiInput, UiPriority],
  template: `
    <ui-card [heading]="'actionDetail.history.title' | transloco" headingId="action-history">
      <ol class="activity">
        @for (line of lines(); track line.id) {
          <li>
            <i aria-hidden="true"></i>
            <div>
              <time [attr.datetime]="line.at">{{ when(line.at) }}</time>
              @if (line.change; as change) {
                <p class="change">
                  @if (change[0]) {
                    <ui-priority [level]="change[0]" />
                    <span aria-hidden="true">→</span>
                    <span class="sr-only">{{ 'actionDetail.history.to' | transloco }}</span>
                  }
                  <ui-priority [level]="change[1]" />
                </p>
              }
              <p>{{ line.key | transloco: params(line) }}</p>
              @if (line.quote) {
                <p class="quote">
                  <q>{{ line.quote }}</q>
                </p>
              }
              @if (line.reason) {
                <small>{{ line.reason | transloco }}</small>
              }
              <small class="event"
                ><span class="kind">{{ line.kind }}</span> <code>{{ line.id }}</code></small
              >
            </div>
          </li>
        } @empty {
          <li class="none">{{ 'actionDetail.history.empty' | transloco }}</li>
        }
      </ol>
      @if (canEdit()) {
        <form class="note" (submit)="add($event, text)">
          <label class="sr-only" for="action-note">{{
            'actionDetail.note.label' | transloco
          }}</label>
          <textarea
            uiInput
            id="action-note"
            rows="2"
            maxlength="2000"
            [placeholder]="'actionDetail.note.placeholder' | transloco"
            #text
          ></textarea>
          <button uiButton type="submit" [loading]="busy()">
            {{ 'actionDetail.note.save' | transloco }}
          </button>
        </form>
      }
    </ui-card>
  `,
  styles: `
    .activity li {
      position: relative;
      display: grid;
      grid-template-columns: 12px minmax(0, 1fr);
      column-gap: var(--space-md);
      padding-bottom: var(--space-lg);
    }
    .activity li:last-child {
      padding-bottom: 0;
    }
    .activity li::before {
      content: '';
      position: absolute;
      left: 5.5px;
      top: 14px;
      bottom: -6px;
      width: 1px;
      background: var(--border);
    }
    .activity li:last-child::before {
      content: none;
    }
    i {
      width: 8px;
      height: 8px;
      margin: 6px 2px 0;
      border-radius: 50%;
      background: var(--control);
    }
    li:first-child i {
      background: var(--text);
    }
    time,
    small {
      display: block;
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .change {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 6px;
    }
    .quote {
      color: var(--muted);
      overflow-wrap: anywhere;
    }
    .event {
      display: flex;
      gap: 6px;
      min-width: 0;
      color: var(--faint);
    }
    .kind,
    code {
      font: 400 11px/16px var(--mono);
    }
    code {
      min-width: 0;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .none {
      color: var(--muted);
    }
    .note {
      display: grid;
      justify-items: start;
      gap: var(--space-sm);
      margin-top: var(--space-xl);
    }
  `,
})
export class ActionHistory {
  readonly lines = input.required<readonly HistoryLine[]>();
  readonly canEdit = input(false);
  readonly busy = input(false);
  readonly note = output<string>();

  private readonly transloco = inject(TranslocoService);

  /** A person who left, or none, is «Someone». */
  protected params(line: HistoryLine): HistoryLine['params'] {
    return line.params['who'] === 'someone'
      ? { ...line.params, who: this.transloco.translate('actionDetail.history.someone') }
      : line.params;
  }

  protected when(value: string): string {
    return new Intl.DateTimeFormat(this.transloco.getActiveLang(), {
      dateStyle: 'medium',
      timeStyle: 'short',
    }).format(new Date(value));
  }

  protected add(event: Event, field: HTMLTextAreaElement): void {
    event.preventDefault();
    const text = field.value.trim();
    if (!text) {
      field.focus();
      return;
    }
    this.note.emit(text);
  }

  /** Called by the page once the note is saved. */
  clear(): void {
    const field = document.getElementById('action-note') as HTMLTextAreaElement | null;
    if (field) field.value = '';
  }
}
