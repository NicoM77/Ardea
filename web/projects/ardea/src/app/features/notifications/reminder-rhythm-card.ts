import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { UiCard, UiInput, UiLoader, UiNotice, UiPriority, UiToaster } from 'ui';
import { firstValueFrom } from 'rxjs';
import { getReminderRhythmResource, NotificationsService } from '../../core/api/client';
import { problemMessageKey } from '../../core/api/problem';
import { Dates } from '../../core/format/dates';
import { priorityOfLevel } from '../actions/action-format';
import {
  DEFAULT_RHYTHMS,
  isRhythm,
  REMINDER_LEVELS,
  REMINDER_RHYTHM_LABELS,
  REMINDER_RHYTHMS,
  type ReminderLevelField,
  type ReminderRhythm,
} from './notify-format';

@Component({
  selector: 'ardea-reminder-rhythm-card',
  imports: [TranslocoPipe, UiCard, UiInput, UiLoader, UiNotice, UiPriority],
  template: `
    <ui-card [heading]="'notify.reminders.title' | transloco">
      <span uiCardMeta>{{ 'notify.reminders.meta' | transloco }}</span>
      <p class="lead">{{ 'notify.reminders.lead' | transloco }}</p>
      <div class="live" role="status">
        @if (problem()) {
          <ui-notice tone="error">{{ problem() | transloco }}</ui-notice>
        }
      </div>
      @if (rhythm.hasValue() || rhythm.error()) {
        @if (rhythm.error()) {
          <ui-notice tone="error">{{ 'notify.loadFailed' | transloco }}</ui-notice>
        } @else {
          <div>
            <ul class="levels">
              @for (l of levels; track l.field) {
                <li>
                  <label class="level" [for]="'rhythm-' + l.level">
                    <ui-priority [level]="priority(l.level)" />
                  </label>
                  <select
                    uiInput
                    class="choice"
                    [id]="'rhythm-' + l.level"
                    [name]="l.field"
                    (change)="choose(l.field, $any($event.target).value)"
                  >
                    @for (r of rhythms; track r) {
                      <option [value]="r" [selected]="draft()[l.field] === r">
                        {{ rhythmLabels[r] | transloco }}
                      </option>
                    }
                  </select>
                </li>
              }
            </ul>
            <p class="note">{{ 'notify.reminders.below' | transloco }}</p>
            @if (nextRun(); as next) {
              <p class="note">{{ 'notify.reminders.next' | transloco: { date: next } }}</p>
            }
          </div>
        }
      } @else {
        <ui-loader [label]="'notify.loading' | transloco" />
      }
    </ui-card>
  `,
  styles: `
    :host {
      display: block;
    }
    .lead,
    .note {
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .lead {
      margin-bottom: var(--space-md);
    }
    .live:empty {
      display: none;
    }
    .live {
      display: grid;
      gap: var(--space-sm);
      margin-bottom: var(--space-md);
    }
    .levels {
      display: grid;
    }
    .levels li {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-md);
      min-height: 52px;
      padding: var(--space-xs) 0;
    }
    .levels li + li {
      border-top: 1px solid var(--rule);
    }
    .choice {
      width: auto;
      min-width: 160px;
    }
    .note {
      margin-top: var(--space-sm);
    }
  `,
})
export class ReminderRhythmCard {
  private readonly api = inject(NotificationsService);
  private readonly transloco = inject(TranslocoService);
  private readonly dates = inject(Dates);
  private readonly toaster = inject(UiToaster);
  private queue: Promise<void> = Promise.resolve();
  private pending = 0;

  readonly organizationId = input.required<string>();

  protected readonly rhythm = getReminderRhythmResource(computed(() => this.organizationId()));
  protected readonly levels = REMINDER_LEVELS;
  protected readonly rhythms = REMINDER_RHYTHMS;
  protected readonly rhythmLabels = REMINDER_RHYTHM_LABELS;
  protected readonly problem = signal<string | null>(null);
  protected readonly draft = signal<Record<ReminderLevelField, ReminderRhythm>>({
    ...DEFAULT_RHYTHMS,
  });

  private readonly stored = computed<Record<ReminderLevelField, ReminderRhythm>>(() => {
    const value = this.rhythm.hasValue() ? this.rhythm.value() : undefined;
    return {
      level5: isRhythm(value?.level5) ? value.level5 : DEFAULT_RHYTHMS.level5,
      level4: isRhythm(value?.level4) ? value.level4 : DEFAULT_RHYTHMS.level4,
      level3: isRhythm(value?.level3) ? value.level3 : DEFAULT_RHYTHMS.level3,
    };
  });

  protected readonly nextRun = computed(() => {
    const next = this.rhythm.hasValue() ? this.rhythm.value()?.nextRunAt : null;
    return next ? this.dates.dateTime(next, 'full') : null;
  });

  constructor() {
    effect(() => {
      const stored = this.stored();
      if (this.pending === 0) this.draft.set({ ...stored });
    });
  }

  protected priority(level: number) {
    return priorityOfLevel(level);
  }

  protected choose(field: ReminderLevelField, value: string): void {
    if (!isRhythm(value)) return;
    const before = { ...this.draft() };
    if (before[field] === value) return;
    const after = { ...before, [field]: value };
    this.draft.set(after);
    this.enqueue(before, after, true);
  }

  private enqueue(
    before: Record<ReminderLevelField, ReminderRhythm>,
    after: Record<ReminderLevelField, ReminderRhythm>,
    announce: boolean,
  ): void {
    this.pending++;
    this.queue = this.queue
      .then(() => this.save(before, after, announce))
      .finally(() => {
        this.pending--;
      });
  }

  private async save(
    before: Record<ReminderLevelField, ReminderRhythm>,
    after: Record<ReminderLevelField, ReminderRhythm>,
    announce: boolean,
  ): Promise<void> {
    this.problem.set(null);
    try {
      const result = await firstValueFrom(this.api.setReminderRhythm(this.organizationId(), after));
      this.rhythm.set(result);
      if (announce) {
        this.toaster.show({
          text: this.transloco.translate('notify.reminders.saved'),
          undo: () => {
            this.draft.set({ ...before });
            this.enqueue(after, before, false);
          },
        });
      }
    } catch (error) {
      this.problem.set(problemMessageKey(error, { fallback: 'notify.reminders.saveFailed' }));
      this.draft.set({ ...before });
    }
  }
}
