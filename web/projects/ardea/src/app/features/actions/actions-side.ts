import { Component, computed, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { UiAvatar, UiCard, UiPriority } from 'ui';
import type { ActionSummary, MemberView } from '../../core/api/client';
import { closeSpeeds, ownerGroups } from './action-format';
import { Duration } from './duration';

/**
 * Who holds the actions in view (mockup «Owners»): one block per action in its priority's colour,
 * unassigned first.
 */
@Component({
  selector: 'ardea-owners-card',
  imports: [TranslocoPipe, UiAvatar, UiCard],
  template: `
    <ui-card [heading]="'actions.owners.title' | transloco">
      <span uiCardMeta>{{ 'actions.owners.meta' | transloco: { count: rows().length } }}</span>
      <ul class="rows">
        @for (group of groups(); track group.userId) {
          <li>
            @if (group.userId && group.name) {
              <ui-avatar [name]="group.name" [personId]="group.userId" />
              <b>{{ group.name }}</b>
            } @else {
              <span class="nobody" aria-hidden="true"></span>
              <b class="muted">{{ 'actions.owners.unassigned' | transloco }}</b>
            }
            <span class="blocks" aria-hidden="true">
              @for (p of group.priorities; track $index) {
                <i [attr.data-p]="p"></i>
              }
            </span>
            <span class="count">{{ group.priorities.length }}</span>
          </li>
        } @empty {
          <li class="muted">{{ 'actions.owners.none' | transloco }}</li>
        }
      </ul>
    </ui-card>
  `,
  styles: `
    .rows li {
      display: flex;
      align-items: center;
      gap: var(--space-md);
      min-height: 44px;
    }
    .rows li + li {
      border-top: 1px solid var(--rule);
    }
    b {
      flex: 1;
      min-width: 0;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      font-weight: 500;
    }
    .muted {
      color: var(--muted);
    }
    .nobody {
      width: 24px;
      height: 24px;
      flex: none;
      border: 1px dashed var(--control);
      border-radius: 50%;
    }
    .blocks {
      display: flex;
      gap: 2px;
    }
    .blocks i {
      width: 12px;
      height: 12px;
      border-radius: 2px;
      background: var(--low);
    }
    .blocks i[data-p='critical'] {
      background: var(--critical);
    }
    .blocks i[data-p='high'] {
      background: var(--high);
    }
    .blocks i[data-p='medium'] {
      background: var(--medium);
    }
    .count {
      min-width: 16px;
      text-align: right;
      font-weight: 500;
    }
  `,
})
export class OwnersCard {
  readonly rows = input.required<readonly ActionSummary[]>();
  readonly members = input<readonly MemberView[]>([]);
  protected readonly groups = computed(() => ownerGroups(this.rows(), this.members()));
}

/**
 * How fast actions close per priority (mockup «Time to close»): the median time the closed actions
 * took against the median time their deadline allowed.
 */
@Component({
  selector: 'ardea-close-speed-card',
  imports: [TranslocoPipe, UiCard, UiPriority, Duration],
  template: `
    <ui-card [heading]="'actions.speed.title' | transloco">
      <span uiCardMeta>{{ 'actions.speed.meta' | transloco: { count: closed().length } }}</span>
      <ul class="speeds">
        @for (speed of speeds(); track speed.priority) {
          <li [attr.data-p]="speed.priority">
            <ui-priority [level]="speed.priority" />
            @if (speed.took !== null) {
              <span class="figure">
                <ardea-duration [ms]="speed.took" />
                @if (speed.allowed) {
                  <span class="of"
                    >{{ 'actions.table.of' | transloco }}
                    <ardea-duration [ms]="speed.allowed" [parts]="1"
                  /></span>
                }
              </span>
              <span
                class="meter"
                role="img"
                [attr.aria-label]="
                  'actions.speed.share'
                    | transloco: { percent: share(speed.took, speed.allowed), count: speed.count }
                "
                ><i [style.transform]="'scaleX(' + ratio(speed.took, speed.allowed) + ')'"></i
              ></span>
            } @else {
              <span class="of">{{ 'actions.speed.none' | transloco }}</span>
            }
          </li>
        }
      </ul>
    </ui-card>
  `,
  styles: `
    .speeds {
      display: grid;
      gap: var(--space-md);
    }
    li {
      display: grid;
      grid-template-columns: minmax(0, 1fr) auto;
      align-items: baseline;
      gap: 6px var(--space-sm);
    }
    .figure {
      font-weight: 500;
      text-align: right;
    }
    .of {
      margin-left: 4px;
      color: var(--muted);
      font-size: var(--fs-sub);
      font-weight: 400;
    }
    .meter {
      grid-column: 1 / -1;
      display: block;
      height: 4px;
      border-radius: var(--radius-meter);
      background: var(--rule);
      overflow: hidden;
    }
    .meter i {
      display: block;
      height: 100%;
      background: var(--positive);
      transform-origin: left;
    }
  `,
})
export class CloseSpeedCard {
  readonly closed = input.required<readonly ActionSummary[]>();
  protected readonly speeds = computed(() => closeSpeeds(this.closed()));

  protected ratio(took: number, allowed: number | null): number {
    return allowed ? Math.min(1, took / allowed) : 1;
  }

  protected share(took: number, allowed: number | null): number {
    return Math.round(this.ratio(took, allowed) * 100);
  }
}
