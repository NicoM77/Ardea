import { Component, computed, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { UiPriority } from 'ui';
import { ACTION_PRIORITIES, type ActionPriority } from '../actions/action-format';

const R = 50;
const LENGTH = 2 * Math.PI * R;
const GAP = 3;

@Component({
  selector: 'ardea-priority-ring',
  imports: [RouterLink, TranslocoPipe, UiPriority],
  template: `
    <div class="ring">
      <svg viewBox="0 0 116 116" width="116" height="116" aria-hidden="true">
        <circle class="track" cx="58" cy="58" r="50" />
        @for (s of segments(); track s.priority) {
          <circle
            cx="58"
            cy="58"
            r="50"
            [attr.data-p]="s.priority"
            [attr.stroke-dasharray]="s.dash"
            [attr.stroke-dashoffset]="s.offset"
            [class.dim]="focus() && focus() !== s.priority"
          />
        }
      </svg>
      <p class="total">
        <b>{{ total() }}</b><span>{{ 'overview.ring.open' | transloco }}</span>
      </p>
    </div>
    <ul class="legend">
      @for (p of priorities; track p) {
        <li>
          <a
            routerLink="../reminders"
            [queryParams]="{ priority: p }"
            (pointerenter)="focused.emit(p)"
            (pointerleave)="focused.emit(null)"
            (focus)="focused.emit(p)"
            (blur)="focused.emit(null)"
          >
            <ui-priority [level]="p" />
            <b>{{ counts()[p] }}</b>
          </a>
        </li>
      }
    </ul>
  `,
  styles: `
    :host {
      display: flex;
      align-items: center;
      gap: var(--gap);
    }
    .ring {
      position: relative;
      width: 116px;
      height: 116px;
      flex: none;
    }
    svg {
      transform: rotate(-90deg);
    }
    circle {
      fill: none;
      stroke-width: 8;
      stroke: var(--low);
      transition: opacity var(--t-ui) ease;
    }
    .track {
      stroke: var(--rule);
    }
    circle[data-p='critical'] {
      stroke: var(--critical);
    }
    circle[data-p='high'] {
      stroke: var(--high);
    }
    circle[data-p='medium'] {
      stroke: var(--medium);
    }
    .dim {
      opacity: 0.2;
    }
    .total {
      position: absolute;
      inset: 0;
      display: grid;
      place-content: center;
      text-align: center;
    }
    .total b {
      font-size: var(--fs-kpi);
      line-height: var(--lh-kpi);
      font-weight: 500;
    }
    .total span {
      color: var(--muted);
      font-size: var(--fs-small);
      line-height: var(--lh-small);
    }
    .legend {
      flex: 1;
      min-width: 0;
    }
    .legend a {
      display: flex;
      align-items: center;
      justify-content: space-between;
      min-height: 32px;
      margin: 0 calc(-1 * var(--space-sm));
      padding: 0 var(--space-sm);
      border-radius: var(--radius-control);
      transition: background-color var(--t-hover) ease;
    }
    .legend b {
      font-weight: 500;
    }
    @media (hover: hover) and (pointer: fine) {
      .legend a:hover {
        background: var(--raised);
      }
    }
  `,
})
export class PriorityRing {
  readonly counts = input.required<Record<ActionPriority, number>>();
  readonly focus = input<string | null>(null);
  readonly focused = output<ActionPriority | null>();

  protected readonly priorities = ACTION_PRIORITIES;
  protected readonly total = computed(() =>
    ACTION_PRIORITIES.reduce((n, p) => n + this.counts()[p], 0),
  );
  protected readonly segments = computed(() => {
    const total = this.total();
    let offset = 0;
    return ACTION_PRIORITIES.filter((p) => this.counts()[p] > 0).map((priority) => {
      const length = (this.counts()[priority] / total) * LENGTH;
      const segment = {
        priority,
        dash: `${Math.max(0, length - (total > this.counts()[priority] ? GAP : 0))} ${LENGTH}`,
        offset: -offset,
      };
      offset += length;
      return segment;
    });
  });
}
