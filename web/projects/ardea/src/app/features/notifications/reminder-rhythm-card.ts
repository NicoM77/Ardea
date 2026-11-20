import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { UiButton, UiCard, UiInput, UiLoader, UiNotice, UiPriority } from 'ui';
import { firstValueFrom } from 'rxjs';
import { getReminderRhythmResource, NotificationsService } from '../../core/api/client';
import { problemMessageKey } from '../../core/api/problem';
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

/**
 * «Reminders by level» on the rules page (master plan 8.7, 17.5; task 5a.6, ADR 048): how often an open reminder reminds
 * the people its rules name, per level, or not at all. Runs come on weekdays at 08:00 in the organization's time zone; below
 * Medium only the digests name a reminder.
 */
@Component({
  selector: 'ardea-reminder-rhythm-card',
  imports: [TranslocoPipe, UiButton, UiCard, UiInput, UiLoader, UiNotice, UiPriority],
  template: `
    <ui-card [heading]="'notify.reminders.title' | transloco">
      <span uiCardMeta>{{ 'notify.reminders.meta' | transloco }}</span>
      <p class="lead">{{ 'notify.reminders.lead' | transloco }}</p>
      <div class="live" role="status">
        @if (saved()) {
          <ui-notice tone="success">{{ 'notify.reminders.saved' | transloco }}</ui-notice>
        }
        @if (problem()) {
          <ui-notice tone="error">{{ problem() | transloco }}</ui-notice>
        }
      </div>
      @if (rhythm.hasValue() || rhythm.error()) {
        @if (rhythm.error()) {
          <ui-notice tone="error">{{ 'notify.loadFailed' | transloco }}</ui-notice>
        } @else {
          <form (submit)="save($event)">
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
                    [disabled]="saving()"
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
            <div class="buttons">
              <button
                uiButton
                variant="primary"
                type="submit"
                [loading]="saving()"
                [disabled]="!changed()"
              >
                {{ 'notify.reminders.save' | transloco }}
              </button>
            </div>
          </form>
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
    .buttons {
      display: flex;
      margin-top: var(--space-md);
    }
  `,
})
export class ReminderRhythmCard {
  private readonly api = inject(NotificationsService);
  private readonly transloco = inject(TranslocoService);

  /** The organization whose rhythm this is. */
  readonly organizationId = input.required<string>();

  protected readonly rhythm = getReminderRhythmResource(computed(() => this.organizationId()));
  protected readonly levels = REMINDER_LEVELS;
  protected readonly rhythms = REMINDER_RHYTHMS;
  protected readonly rhythmLabels = REMINDER_RHYTHM_LABELS;
  protected readonly saving = signal(false);
  protected readonly saved = signal(false);
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

  protected readonly changed = computed(() =>
    REMINDER_LEVELS.some((l) => this.draft()[l.field] !== this.stored()[l.field]),
  );

  protected readonly nextRun = computed(() => {
    const next = this.rhythm.hasValue() ? this.rhythm.value()?.nextRunAt : null;
    return next
      ? new Intl.DateTimeFormat(this.transloco.getActiveLang(), {
          dateStyle: 'full',
          timeStyle: 'short',
        }).format(new Date(next))
      : null;
  });

  constructor() {
    effect(() => this.draft.set({ ...this.stored() }));
  }

  protected priority(level: number) {
    return priorityOfLevel(level);
  }

  protected choose(field: ReminderLevelField, value: string): void {
    if (!isRhythm(value)) return;
    this.saved.set(false);
    this.draft.update((d) => ({ ...d, [field]: value }));
  }

  protected async save(event: Event): Promise<void> {
    event.preventDefault();
    if (this.saving() || !this.changed()) return;
    this.saving.set(true);
    this.saved.set(false);
    this.problem.set(null);
    try {
      const result = await firstValueFrom(
        this.api.setReminderRhythm(this.organizationId(), { ...this.draft() }),
      );
      this.rhythm.set(result);
      this.saved.set(true);
    } catch (error) {
      this.problem.set(problemMessageKey(error, { fallback: 'notify.reminders.saveFailed' }));
    } finally {
      this.saving.set(false);
    }
  }
}
