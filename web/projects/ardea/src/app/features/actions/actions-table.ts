import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { UiAvatar, UiPill, UiPriority, UiStatus } from 'ui';
import type { ActionSummary, MemberView } from '../../core/api/client';
import {
  actionName,
  clockStart,
  entriesLine,
  num,
  priorityOf,
  statusKindOf,
  timeLeft,
  usedShare,
} from './action-format';
import { Duration } from './duration';

interface Row {
  action: ActionSummary;
  number: number;
  name: ReturnType<typeof actionName>;
  entries: ReturnType<typeof entriesLine>;
  owner: { id: string; name: string } | null;
  left: number | null;
  used: number;
  took: number | null;
  allowed: number | null;
}

/**
 * The actions as a real table (master plan 11.8): priority, the action with its project, version
 * and entry, the time left with the share used (or, closed, the time it took against the time it
 * had), owner and status. Below 640px the rows become two-line records. The title is the link; the
 * whole row answers the pointer.
 */
@Component({
  selector: 'ardea-actions-table',
  imports: [RouterLink, TranslocoPipe, UiAvatar, UiPill, UiPriority, UiStatus, Duration],
  template: `
    <table>
      <caption class="sr-only">
        {{
          caption()
        }}
      </caption>
      <thead>
        <tr>
          <th scope="col">{{ 'actions.table.priority' | transloco }}</th>
          <th scope="col">{{ 'actions.table.action' | transloco }}</th>
          <th scope="col">
            {{ (closed() ? 'actions.table.took' : 'actions.table.left') | transloco }}
          </th>
          <th scope="col">{{ 'actions.table.owner' | transloco }}</th>
          <th scope="col">{{ 'actions.table.status' | transloco }}</th>
        </tr>
      </thead>
      <tbody>
        @for (row of shown(); track row.number) {
          <tr [attr.data-priority]="row.action.levelName">
            <td class="prio"><ui-priority [level]="priority(row.action.levelName)" /></td>
            <td class="main">
              <span class="title">
                <a [routerLink]="[row.number]">{{ row.name.key | transloco: row.name.params }}</a>
                @if (row.action.exploitation === 'active') {
                  <ui-pill tone="alert">{{ 'actions.pill.exploited' | transloco }}</ui-pill>
                }
                @if (!row.action.fixAvailable) {
                  <ui-pill>{{ 'actions.pill.noFix' | transloco }}</ui-pill>
                }
              </span>
              <span class="sub">
                {{ row.action.project.name }}
                @if (row.action.component.version) {
                  · <span class="id">{{ row.action.component.version }}</span>
                }
                @if (row.entries.first) {
                  · <span class="id">{{ row.entries.first }}</span>
                  @if (row.entries.more) {
                    +{{ row.entries.more }}
                  }
                }
              </span>
            </td>
            <td class="due">
              @if (closed()) {
                @if (row.took !== null) {
                  <span class="line">
                    <ardea-duration [ms]="row.took" />
                    @if (row.allowed) {
                      <span class="of">
                        {{ 'actions.table.of' | transloco }}
                        <ardea-duration [ms]="row.allowed" [parts]="1" />
                      </span>
                    }
                  </span>
                }
              } @else if (row.left !== null) {
                <span class="line" [class.over]="row.left < 0">
                  @if (row.left < 0) {
                    <span>{{ 'actions.table.overdue' | transloco }}</span>
                  }
                  <ardea-duration [ms]="row.left" />
                  <span class="sr-only">{{ 'actions.table.leftWord' | transloco }}</span>
                </span>
                <span
                  class="meter"
                  role="img"
                  [attr.aria-label]="
                    'actions.table.used' | transloco: { percent: percent(row.used) }
                  "
                  ><i [style.transform]="'scaleX(' + row.used + ')'"></i
                ></span>
              } @else {
                <span class="muted">{{ 'actions.table.noDeadline' | transloco }}</span>
              }
            </td>
            <td class="owner">
              @if (row.owner; as owner) {
                <span class="who" [title]="owner.name">
                  <ui-avatar [name]="owner.name" [personId]="owner.id" />
                  <span class="sr-only">{{ owner.name }}</span>
                </span>
              } @else {
                <span class="muted">–</span>
                <span class="sr-only">{{ 'actions.table.unassigned' | transloco }}</span>
              }
            </td>
            <td class="status">
              <ui-status [kind]="status(row.action)" />
            </td>
          </tr>
        }
      </tbody>
    </table>
  `,
  styles: `
    table {
      width: 100%;
      border-collapse: collapse;
    }
    th {
      height: 36px;
      padding: 0 var(--space-lg) 0 0;
      color: var(--muted);
      font-size: var(--fs-small);
      font-weight: 400;
      text-align: left;
      white-space: nowrap;
    }
    th:first-child {
      width: 108px;
    }
    th:nth-child(3) {
      width: 176px;
    }
    th:nth-child(4) {
      width: 56px;
    }
    th:last-child {
      width: 128px;
      padding-right: 0;
    }
    tr {
      position: relative;
    }
    td {
      height: 64px;
      padding: var(--space-sm) var(--space-lg) var(--space-sm) 0;
      border-top: 1px solid var(--rule);
      vertical-align: middle;
    }
    td:last-child {
      padding-right: 0;
    }
    tbody tr {
      transition: background-color var(--t-hover) ease;
    }
    .main {
      min-width: 0;
    }
    .title {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-xs) var(--space-sm);
    }
    .title a {
      font-weight: 500;
      overflow-wrap: anywhere;
    }
    .title a::after {
      content: '';
      position: absolute;
      inset: 0;
    }
    .title a:focus-visible {
      outline: none;
    }
    .title a:focus-visible::after {
      outline: var(--focus-ring);
      outline-offset: -2px;
      border-radius: var(--radius-control);
    }
    .sub {
      display: block;
      margin-top: 2px;
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .line {
      display: flex;
      flex-wrap: wrap;
      align-items: baseline;
      gap: 0 var(--space-xs);
      font-weight: 500;
    }
    .of {
      margin-left: 4px;
    }
    .of,
    .muted {
      color: var(--muted);
      font-size: var(--fs-sub);
      font-weight: 400;
    }
    .meter {
      display: block;
      height: 4px;
      margin-top: 6px;
      border-radius: var(--radius-meter);
      background: var(--rule);
      overflow: hidden;
    }
    .meter i {
      display: block;
      height: 100%;
      border-radius: var(--radius-meter);
      background: var(--c, var(--muted));
      transform-origin: left;
    }
    tr[data-priority='critical'] {
      --c: var(--critical);
    }
    tr[data-priority='high'] {
      --c: var(--high);
    }
    tr[data-priority='medium'] {
      --c: var(--medium);
    }
    .who {
      display: inline-flex;
    }
    @media (hover: hover) and (pointer: fine) {
      tbody tr:hover {
        background: var(--raised);
      }
    }
    @media (max-width: 640px) {
      thead {
        position: absolute;
        width: 1px;
        height: 1px;
        overflow: hidden;
        clip: rect(0 0 0 0);
      }
      tbody tr {
        display: grid;
        grid-template-columns: minmax(0, 1fr) auto;
        gap: var(--space-xs) var(--space-md);
        padding: var(--space-md) 0;
        border-top: 1px solid var(--rule);
      }
      td {
        display: block;
        height: auto;
        padding: 0;
        border: 0;
      }
      .prio {
        grid-column: 1;
      }
      .main {
        grid-column: 1 / -1;
        grid-row: 2;
      }
      .due {
        grid-column: 1;
        grid-row: 3;
      }
      .owner {
        display: none;
      }
      .status {
        grid-column: 2;
        grid-row: 1;
      }
    }
  `,
})
export class ActionsTable {
  readonly rows = input.required<readonly ActionSummary[]>();
  readonly members = input<readonly MemberView[]>([]);
  readonly closed = input(false);
  readonly now = input.required<Date>();
  /** Already translated: what the table lists, for screen readers. */
  readonly caption = input.required<string>();

  protected readonly shown = computed<Row[]>(() => {
    const now = this.now();
    const members = this.members();
    return this.rows().map((action) => {
      const member = action.assignedTo
        ? members.find((m) => m.userId === action.assignedTo)
        : undefined;
      const name = member?.displayName ?? null;
      const start = clockStart(action);
      return {
        action,
        number: num(action.number),
        name: actionName(action),
        entries: entriesLine(action),
        owner: action.assignedTo && name ? { id: action.assignedTo, name } : null,
        left: action.dueAt ? timeLeft(action.dueAt, now) : null,
        used: action.dueAt ? usedShare(start, action.dueAt, now) : 0,
        took: action.closedAt
          ? new Date(action.closedAt).getTime() - new Date(action.openedAt).getTime()
          : null,
        allowed: action.dueAt ? new Date(action.dueAt).getTime() - new Date(start).getTime() : null,
      };
    });
  });

  protected readonly priority = priorityOf;

  protected status(action: ActionSummary) {
    return statusKindOf(action.status, action.closeReason);
  }

  protected percent(share: number): number {
    return Math.round(share * 100);
  }
}
