import { Component, inject, input, output, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { LucideCircleCheck, LucideRotateCw } from '@lucide/angular';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiCard, UiLoader, UiNotice, UiToaster } from 'ui';
import { AdministrationService, type AbandonedJobResponse } from '../../core/api/client';
import { problemMessageKey } from '../../core/api/problem';
import { Dates } from '../../core/format/dates';
import { count } from './settings-patch';

@Component({
  selector: 'ardea-jobs-card',
  imports: [TranslocoPipe, LucideCircleCheck, LucideRotateCw, UiButton, UiCard, UiLoader, UiNotice],
  template: `
    <ui-card [heading]="'admin.jobs.title' | transloco" [attr.aria-busy]="loading()">
      @if (jobs()?.length) {
        <span uiCardMeta>{{ 'admin.jobs.count' | transloco: { count: jobs()!.length } }}</span>
      }

      @if (problem()) {
        <ui-notice class="gap" tone="error">{{ problem() | transloco }}</ui-notice>
      }

      @if (failed()) {
        <ui-notice tone="error">{{ 'admin.jobs.loadFailed' | transloco }}</ui-notice>
        <div class="gap">
          <button uiButton type="button" (click)="reload.emit()">
            {{ 'admin.tryAgain' | transloco }}
          </button>
        </div>
      } @else if (jobs(); as list) {
        @if (list.length === 0) {
          <p class="healthy">
            <svg lucideCircleCheck aria-hidden="true"></svg>
            {{ 'admin.jobs.empty' | transloco }}
          </p>
        } @else {
          <div class="table-wrap">
            <table>
              <caption class="sr-only">
                {{
                  'admin.jobs.title' | transloco
                }}
              </caption>
              <thead>
                <tr>
                  <th scope="col">{{ 'admin.jobs.kind' | transloco }}</th>
                  <th scope="col">{{ 'admin.jobs.state' | transloco }}</th>
                  <th scope="col" class="num">{{ 'admin.jobs.attempts' | transloco }}</th>
                  <th scope="col">{{ 'admin.jobs.finished' | transloco }}</th>
                  <th scope="col">
                    <span class="sr-only">{{ 'admin.jobs.actions' | transloco }}</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                @for (job of list; track job.id) {
                  <tr>
                    <td class="main">
                      <span class="id">{{ job.kind }}</span>
                      @if (job.lastError) {
                        <span class="error">{{ job.lastError }}</span>
                      }
                    </td>
                    <td>{{ stateLabel(job.state) | transloco }}</td>
                    <td class="num">
                      {{
                        'admin.jobs.attemptsOf'
                          | transloco
                            : { attempts: number(job.attempts), max: number(job.maxAttempts) }
                      }}
                    </td>
                    <td class="when">
                      @if (job.finishedAt) {
                        <time [attr.datetime]="job.finishedAt">{{
                          dates.dateTime(job.finishedAt)
                        }}</time>
                      } @else {
                        <span class="muted">–</span>
                      }
                    </td>
                    <td class="act">
                      <button
                        uiButton
                        type="button"
                        [loading]="retrying() === job.id"
                        [attr.aria-label]="'admin.jobs.retryLabel' | transloco: { kind: job.kind }"
                        (click)="retry(job)"
                      >
                        <svg lucideRotateCw aria-hidden="true"></svg>
                        {{ 'admin.jobs.retry' | transloco }}
                      </button>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      } @else {
        <ui-loader class="wait" [label]="'admin.jobs.loading' | transloco" />
      }
    </ui-card>
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }
    .gap {
      display: block;
      margin-top: var(--space-md);
    }
    .wait {
      min-height: 24px;
    }
    .healthy {
      display: flex;
      align-items: center;
      gap: var(--space-sm);
      color: var(--muted);
    }
    .healthy svg {
      flex: none;
      width: 16px;
      height: 16px;
      color: var(--positive);
    }
    .table-wrap {
      overflow-x: auto;
      margin: 0 calc(-1 * var(--space-sm));
    }
    table {
      width: 100%;
      border-collapse: collapse;
    }
    th {
      height: 36px;
      padding: 0 var(--space-sm);
      color: var(--muted);
      font-size: var(--fs-small);
      font-weight: 400;
      text-align: left;
      white-space: nowrap;
    }
    td {
      padding: var(--space-md) var(--space-sm);
      border-top: 1px solid var(--rule);
      vertical-align: top;
    }
    .num {
      text-align: right;
      white-space: nowrap;
    }
    .main {
      min-width: 220px;
    }
    .main .id {
      display: block;
      font-weight: 500;
    }
    .error {
      display: block;
      margin-top: 2px;
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
      overflow-wrap: anywhere;
    }
    .when {
      color: var(--muted);
      white-space: nowrap;
    }
    .act {
      text-align: right;
    }
    .muted {
      color: var(--faint);
    }
    @media (max-width: 640px) {
      .table-wrap {
        margin: 0;
        overflow: visible;
      }
      table,
      tbody,
      tr,
      td {
        display: block;
      }
      thead {
        position: absolute;
        width: 1px;
        height: 1px;
        overflow: hidden;
        clip: rect(0 0 0 0);
      }
      tr {
        display: grid;
        grid-template-columns: minmax(0, 1fr) auto;
        gap: var(--space-xs) var(--space-md);
        padding: var(--space-md) 0;
        border-top: 1px solid var(--rule);
      }
      td {
        padding: 0;
        border: 0;
        text-align: left;
      }
      .main {
        grid-column: 1 / -1;
        min-width: 0;
      }
      .when {
        grid-column: 1;
      }
      .act {
        grid-column: 1 / -1;
        text-align: left;
        margin-top: var(--space-xs);
      }
    }
  `,
})
export class JobsCard {
  readonly jobs = input<AbandonedJobResponse[] | null>(null);
  readonly loading = input(false);
  readonly failed = input(false);
  readonly reload = output();

  private readonly admin = inject(AdministrationService);
  private readonly transloco = inject(TranslocoService);
  private readonly toaster = inject(UiToaster);
  protected readonly dates = inject(Dates);

  protected readonly number = count;
  protected readonly retrying = signal<string | null>(null);
  protected readonly problem = signal<string | null>(null);
  protected stateLabel(state: string): string {
    return state === 'dead' ? 'admin.jobs.dead' : 'admin.jobs.failed';
  }

  protected async retry(job: AbandonedJobResponse): Promise<void> {
    this.problem.set(null);
    this.retrying.set(job.id);
    try {
      await firstValueFrom(this.admin.retryJob(job.id));
      this.toaster.show({
        text: this.transloco.translate('admin.jobs.retried', { kind: job.kind }),
      });
    } catch (error) {
      this.problem.set(
        problemMessageKey(error, {
          statuses: { 404: 'admin.jobs.gone' },
          fallback: 'admin.jobs.retryFailed',
        }),
      );
    } finally {
      this.retrying.set(null);
      this.reload.emit();
    }
  }
}
