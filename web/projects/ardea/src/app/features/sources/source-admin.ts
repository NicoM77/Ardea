import { Component, computed, inject, input, output, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { LucidePause, LucidePlay, LucideRefreshCw } from '@lucide/angular';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiLoader, UiNotice } from 'ui';
import {
  listQuarantineResource,
  listSourceRunsResource,
  SourcesService,
  type SourceStatus,
} from '../../core/api/client';
import { problemMessageKey } from '../../core/api/problem';
import { webUrl } from '../entries/entry-detail-format';
import { count } from './source-format';

/**
 * What the instance administrator sees of one source (master plan 6.8, 11.4): the recent runs with
 * what they fetched and changed, the quarantine list with the reason each record could not be
 * read, and control: run now, pause or resume.
 */
@Component({
  selector: 'ardea-source-admin',
  imports: [TranslocoPipe, LucidePause, LucidePlay, LucideRefreshCw, UiButton, UiLoader, UiNotice],
  template: `
    <div class="actions">
      <button
        uiButton
        type="button"
        [loading]="busy() === 'sync'"
        [disabled]="!source().enabled"
        (click)="syncNow()"
      >
        <svg lucideRefreshCw aria-hidden="true"></svg>
        {{ 'sources.admin.syncNow' | transloco }}
      </button>
      <button
        uiButton
        variant="ghost"
        type="button"
        [loading]="busy() === 'toggle'"
        (click)="toggle()"
      >
        @if (source().enabled) {
          <svg lucidePause aria-hidden="true"></svg>
          {{ 'sources.admin.pause' | transloco }}
        } @else {
          <svg lucidePlay aria-hidden="true"></svg>
          {{ 'sources.admin.resume' | transloco }}
        }
      </button>
    </div>
    <div class="live" role="status">
      @if (done()) {
        <ui-notice tone="success">{{ done() | transloco }}</ui-notice>
      }
    </div>
    @if (problem()) {
      <ui-notice tone="error">{{ problem() | transloco }}</ui-notice>
    }

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
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-sm);
    }
    .live:empty {
      display: none;
    }
    h4 {
      margin-top: var(--space-sm);
      font-size: var(--fs-body);
      line-height: var(--lh-body);
      font-weight: 600;
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
  /** The source changed (paused, resumed or made due); the list reloads. */
  readonly changed = output();

  private readonly api = inject(SourcesService);
  private readonly transloco = inject(TranslocoService);

  private readonly key = computed(() => this.source().key);
  private readonly limit = signal({ limit: 10 });
  protected readonly runs = listSourceRunsResource(this.key, this.limit);
  protected readonly quarantine = listQuarantineResource(this.key, signal({ limit: 50 }));

  protected readonly busy = signal<'sync' | 'toggle' | null>(null);
  protected readonly done = signal<string | null>(null);
  protected readonly problem = signal<string | null>(null);
  protected readonly number = count;
  protected readonly web = webUrl;

  protected async syncNow(): Promise<void> {
    await this.run('sync', async () => {
      await firstValueFrom(this.api.syncSource(this.source().key));
      return 'sources.admin.syncRequested';
    });
  }

  protected async toggle(): Promise<void> {
    const enabled = !this.source().enabled;
    await this.run('toggle', async () => {
      await firstValueFrom(
        this.api.updateSource(this.source().key, { enabled, intervalMinutes: null }),
      );
      return enabled ? 'sources.admin.resumed' : 'sources.admin.paused';
    });
  }

  private async run(kind: 'sync' | 'toggle', action: () => Promise<string>): Promise<void> {
    this.busy.set(kind);
    this.done.set(null);
    this.problem.set(null);
    try {
      this.done.set(await action());
      this.changed.emit();
      this.runs.reload();
    } catch (error) {
      this.problem.set(
        problemMessageKey(error, {
          codes: { source_disabled: 'sources.admin.disabled', not_found: 'sources.admin.gone' },
          fallback: 'sources.admin.failed',
        }),
      );
    } finally {
      this.busy.set(null);
    }
  }

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
