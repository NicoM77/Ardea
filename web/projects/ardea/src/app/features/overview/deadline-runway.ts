import { Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { UiPriority } from 'ui';
import { actionName, num, timeLeft } from '../actions/action-format';
import { Duration } from '../actions/duration';
import type { RunwayAxis, RunwayRow } from './overview-format';

/**
 * The deadline runway (DESIGN.md Charts, mockup overview.html «Deadlines»): every open action as a
 * bar on one fourteen-day axis, from the start of its clock to its deadline, the used part solid.
 * Each row is a link to the action and says its priority and the time left in words.
 */
@Component({
  selector: 'ardea-deadline-runway',
  imports: [RouterLink, TranslocoPipe, UiPriority, Duration],
  host: { '[attr.data-focus]': 'focus()' },
  template: `
    <div class="axis" aria-hidden="true">
      @for (day of axis().days; track day.at) {
        @if (day.weekend) {
          <span class="weekend" [style.left.%]="day.at" [style.width.%]="100 / 14"></span>
        }
        <span class="day" [style.left.%]="day.at"></span>
      }
      @for (tick of axis().ticks; track tick.at; let first = $first) {
        <span class="tick" [class.first]="first" [style.left.%]="tick.at">{{
          day(tick.date)
        }}</span>
      }
      <span class="now" [style.left.%]="axis().now"
        ><span>{{ 'overview.runway.now' | transloco }}</span></span
      >
    </div>
    <ul class="rows">
      @for (row of rows(); track row.action.number) {
        <li [attr.data-p]="row.priority" [class.is-focus]="focus() === row.priority">
          <a [routerLink]="['../actions', number(row.action.number)]">
            <span class="label">
              <ui-priority [level]="row.priority" symbolOnly />
              <span class="name">{{ name(row).key | transloco: name(row).params }}</span>
            </span>
            <span class="track" aria-hidden="true">
              <span
                class="bar"
                [class.clipped]="row.clipped"
                [style.left.%]="row.from"
                [style.width.%]="row.to - row.from"
                ><i [style.width.%]="row.used"></i
              ></span>
              @if (!row.clipped) {
                <span class="due" [style.left.%]="row.to"></span>
              }
            </span>
            <span class="left">
              @if (left(row) < 0) {
                {{ 'overview.runway.overdue' | transloco }}
              }
              <ardea-duration [ms]="left(row)" [parts]="2" />
              <span class="sr-only">{{ 'overview.runway.left' | transloco }}</span>
            </span>
          </a>
        </li>
      }
    </ul>
  `,
  styleUrl: './deadline-runway.css',
})
export class DeadlineRunway {
  readonly axis = input.required<RunwayAxis>();
  readonly rows = input.required<readonly RunwayRow[]>();
  readonly now = input.required<Date>();
  /** A priority to stand out, from the ring's legend. */
  readonly focus = input<string | null>(null);

  private readonly transloco = inject(TranslocoService);
  private readonly dayFormat = computed(
    () =>
      new Intl.DateTimeFormat(this.transloco.getActiveLang(), { day: 'numeric', month: 'short' }),
  );

  protected readonly number = num;

  protected name(row: RunwayRow) {
    return actionName(row.action);
  }

  protected left(row: RunwayRow): number {
    return timeLeft(row.action.dueAt!, this.now());
  }

  protected day(date: Date): string {
    return this.dayFormat().format(date);
  }
}
