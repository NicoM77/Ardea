import { Component, computed, inject, input, output } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { UiButton, UiCard, UiLoader, UiNotice } from 'ui';
import type { InstanceStatusView, StatusWarning } from '../../core/api/client';
import { groupMetrics, notifiedAt } from './operations';

/**
 * The system's warnings about itself and the operating figures of the instance (master plan 12.7; task
 * 6.6): lag and failures per source, jobs waiting and failed, deliveries, calls to language models. Counts
 * and times only, never contents. Group and figure names are the technical identifiers the server reports.
 */
@Component({
  selector: 'ardea-operations-card',
  imports: [TranslocoPipe, UiButton, UiCard, UiLoader, UiNotice],
  template: `
    <ui-card [heading]="'admin.operations.title' | transloco" [attr.aria-busy]="loading()">
      @if (status(); as view) {
        <span uiCardMeta>
          {{ 'admin.operations.asOf' | transloco: { time: when(view.at) } }}
        </span>
        <div class="warnings" role="status">
          @for (warning of view.warnings; track warning.code + (warning.subject ?? '')) {
            <ui-notice tone="error">
              {{ warning.message }}
              @if (mailed(view, warning); as at) {
                <span class="mailed">
                  {{ 'admin.operations.notified' | transloco: { time: when(at) } }}
                </span>
              }
            </ui-notice>
          } @empty {
            <ui-notice tone="success">{{ 'admin.operations.healthy' | transloco }}</ui-notice>
          }
        </div>
        <div class="groups">
          @for (group of groups(); track group.group) {
            <section>
              <h3 class="id">{{ group.group }}</h3>
              <dl>
                @for (row of group.rows; track row.name) {
                  <div>
                    <dt>{{ row.name }}</dt>
                    <dd>
                      {{ figure(row.value) }}
                      @if (row.unit) {
                        <span class="unit"> {{ row.unit }}</span>
                      }
                    </dd>
                  </div>
                }
              </dl>
            </section>
          }
        </div>
      } @else if (failed()) {
        <ui-notice tone="error">{{ 'admin.operations.loadFailed' | transloco }}</ui-notice>
        <div class="gap">
          <button uiButton type="button" (click)="reload.emit()">
            {{ 'admin.tryAgain' | transloco }}
          </button>
        </div>
      } @else {
        <ui-loader [label]="'admin.operations.loading' | transloco" />
      }
    </ui-card>
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }
    .warnings {
      display: grid;
      gap: var(--space-sm);
      margin-bottom: var(--space-lg);
    }
    .mailed {
      display: block;
      color: var(--muted);
      font-size: var(--fs-small);
    }
    .groups {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
      gap: var(--space-lg) var(--space-2xl);
    }
    h3 {
      margin-bottom: var(--space-sm);
      font-size: var(--fs-body);
      font-weight: 500;
    }
    dl > div {
      display: flex;
      justify-content: space-between;
      gap: var(--space-md);
      padding: var(--space-xs) 0;
      border-top: 1px solid var(--rule);
    }
    dt {
      color: var(--muted);
      overflow-wrap: anywhere;
    }
    dd {
      white-space: nowrap;
      font-variant-numeric: tabular-nums;
    }
    .unit {
      color: var(--muted);
    }
    .gap {
      margin-top: var(--space-md);
    }
  `,
})
export class OperationsCard {
  private readonly transloco = inject(TranslocoService);

  readonly status = input<InstanceStatusView | null>(null);
  readonly loading = input(false);
  readonly failed = input(false);
  readonly reload = output<void>();

  protected readonly groups = computed(() => groupMetrics(this.status()?.metrics ?? []));

  /** When the instance administrators were last mailed about the warning (ADR 043). */
  protected mailed(view: InstanceStatusView, warning: StatusWarning): string | null {
    return notifiedAt(view.notified, warning);
  }

  protected figure(value: number): string {
    return new Intl.NumberFormat(this.transloco.getActiveLang()).format(value);
  }

  protected when(value: string): string {
    return new Intl.DateTimeFormat(this.transloco.getActiveLang(), {
      dateStyle: 'short',
      timeStyle: 'short',
    }).format(new Date(value));
  }
}
