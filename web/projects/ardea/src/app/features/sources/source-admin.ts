import { Component, computed, inject, input, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { UiLoader } from 'ui';
import {
  listQuarantineResource,
  listSourceRunsResource,
  type SourceStatus,
} from '../../core/api/client';
import { webUrl } from '../entries/entry-detail-format';
import { count } from './source-format';

@Component({
  selector: 'ardea-source-admin',
  imports: [TranslocoPipe, UiLoader],
  template: `
    <h4>{{ 'sources.admin.runs' | transloco }}</h4>
    @if (runs.hasValue() && runs.value(); as list) {
      @if (list.length) {
        <div class="table-wrap">
          <table
            [attr.aria-label]="'sources.admin.runsCaption' | transloco: { name: source().name }"
          >
            <thead>
              <tr>
                <th scope="col">{{ 'sources.admin.started' | transloco }}</th>
                <th scope="col">{{ 'sources.admin.mode' | transloco }}</th>
                <th scope="col">{{ 'sources.admin.outcome' | transloco }}</th>
                <th scope="col" class="num">{{ 'sources.admin.fetched' | transloco }}</th>
                <th scope="col" class="num">{{ 'sources.admin.changed' | transloco }}</th>
              </tr>
            </thead>
            <tbody>
              @for (run of list; track run.startedAt) {
                <tr>
                  <td>
                    <time [attr.datetime]="run.startedAt">{{ when(run.startedAt) }}</time>
                  </td>
                  <td>{{ run.mode }}</td>
                  <td>
                    {{ outcomeLabel(run.outcome) | transloco }}
                    @if (run.error) {
                      <span class="sub">{{ run.error }}</span>
                    }
                  </td>
                  <td class="num">{{ number(run.fetched) }}</td>
                  <td class="num">{{ number(run.changed) }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      } @else {
        <p class="muted">{{ 'sources.admin.noRuns' | transloco }}</p>
      }
    } @else if (runs.error()) {
      <p class="muted">{{ 'sources.admin.runsFailed' | transloco }}</p>
    } @else {
      <ui-loader [label]="'sources.admin.loading' | transloco" />
    }

    <h4>{{ 'sources.admin.quarantine' | transloco }}</h4>
    @if (quarantine.hasValue() && quarantine.value(); as list) {
      @if (list.length) {
        <ul class="records">
          @for (record of list; track record.externalId) {
            <li>
              @if (web(record.url); as href) {
                <a class="link mono" [href]="href" target="_blank" rel="noopener noreferrer">{{
                  record.externalId
                }}</a>
              } @else {
                <span class="mono">{{ record.externalId }}</span>
              }
              <span class="reason">{{
                record.reason ?? ('sources.admin.noReason' | transloco)
              }}</span>
              <span class="sub">
                {{
                  'sources.admin.seen'
                    | transloco
                      : { when: when(record.lastSeenAt), revision: number(record.revision) }
                }}
              </span>
            </li>
          }
        </ul>
      } @else {
        <p class="muted">{{ 'sources.admin.noQuarantine' | transloco }}</p>
      }
    } @else if (quarantine.error()) {
      <p class="muted">{{ 'sources.admin.quarantineFailed' | transloco }}</p>
    } @else {
      <ui-loader [label]="'sources.admin.loading' | transloco" />
    }
  `,
  styles: `
    :host {
      display: grid;
      gap: var(--space-md);
      min-width: 0;
    }
    h4 {
      font-size: var(--fs-body);
      line-height: var(--lh-body);
      font-weight: 600;
    }
    h4:not(:first-child) {
      margin-top: var(--space-sm);
    }
    .muted {
      color: var(--muted);
    }
    .sub {
      display: block;
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
      overflow-wrap: anywhere;
    }
    .mono {
      font-family: var(--mono);
      font-size: var(--fs-sub);
      overflow-wrap: anywhere;
    }
    .table-wrap {
      overflow-x: auto;
    }
    table {
      width: 100%;
      border-collapse: collapse;
    }
    th {
      height: 32px;
      padding: 0 var(--space-md) 0 0;
      color: var(--muted);
      font-size: var(--fs-small);
      font-weight: 400;
      text-align: left;
      white-space: nowrap;
    }
    td {
      padding: var(--space-sm) var(--space-md) var(--space-sm) 0;
      border-top: 1px solid var(--rule);
      vertical-align: top;
    }
    .num {
      text-align: right;
    }
    .records {
      display: grid;
    }
    .records li {
      display: grid;
      gap: 2px;
      padding: var(--space-sm) 0;
      border-top: 1px solid var(--rule);
    }
  `,
})
export class SourceAdmin {
  readonly source = input.required<SourceStatus>();

  private readonly transloco = inject(TranslocoService);

  private readonly key = computed(() => this.source().key);
  private readonly limit = signal({ limit: 10 });
  protected readonly runs = listSourceRunsResource(this.key, this.limit);
  protected readonly quarantine = listQuarantineResource(this.key, signal({ limit: 50 }));

  protected readonly number = count;
  protected readonly web = webUrl;

  protected outcomeLabel(outcome: string): string {
    const labels: Record<string, string> = {
      succeeded: 'sources.outcome.succeeded',
      failed: 'sources.outcome.failed',
      running: 'sources.outcome.running',
    };
    return labels[outcome] ?? 'sources.outcome.running';
  }

  private readonly format = computed(
    () =>
      new Intl.DateTimeFormat(this.transloco.getActiveLang(), {
        dateStyle: 'medium',
        timeStyle: 'short',
      }),
  );

  protected when(value: string): string {
    return this.format().format(new Date(value));
  }
}
