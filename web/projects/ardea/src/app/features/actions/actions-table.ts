import { Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { UiPill, UiPriority, UiStatus } from 'ui';
import type { ActionSummary } from '../../core/api/client';
import {
  actionName,
  clockStart,
  entriesLine,
  num,
  priorityOf,
  statusKindOf,
} from './action-format';

interface Row {
  action: ActionSummary;
  number: number;
  name: ReturnType<typeof actionName>;
  entries: ReturnType<typeof entriesLine>;
  since: string;
}

@Component({
  selector: 'ardea-actions-table',
  imports: [RouterLink, TranslocoPipe, UiPill, UiPriority, UiStatus],
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
            {{ (closed() ? 'actions.table.closedOn' : 'actions.table.since') | transloco }}
          </th>
          @if (closed()) {
            <th scope="col">{{ 'actions.table.status' | transloco }}</th>
          }
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
            <td class="since">{{ row.since }}</td>
            @if (closed()) {
              <td class="status">
                <ui-status [kind]="status(row.action)" />
              </td>
            }
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
      width: 136px;
    }
    th:nth-child(4) {
      width: 128px;
    }
    th:last-child {
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
    .since {
      color: var(--muted);
      white-space: nowrap;
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
      .since {
        grid-column: 1;
        grid-row: 3;
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
  readonly closed = input(false);
  readonly caption = input.required<string>();

  private readonly transloco = inject(TranslocoService);

  protected readonly shown = computed<Row[]>(() => {
    const day = new Intl.DateTimeFormat(this.transloco.getActiveLang(), { dateStyle: 'medium' });
    return this.rows().map((action) => ({
      action,
      number: num(action.number),
      name: actionName(action),
      entries: entriesLine(action),
      since: day.format(new Date(action.closedAt ?? clockStart(action))),
    }));
  });

  protected readonly priority = priorityOf;

  protected status(action: ActionSummary) {
    return statusKindOf(action.status, action.closeReason);
  }
}
