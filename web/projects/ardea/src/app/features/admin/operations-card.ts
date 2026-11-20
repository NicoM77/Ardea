import { Component, computed, inject, input, output } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { LucideCircleCheck } from '@lucide/angular';
import { UiButton, UiCard, UiLoader, UiNotice } from 'ui';
import type { InstanceStatusView, StatusWarning } from '../../core/api/client';
import { Dates } from '../../core/format/dates';
import { groupMetrics, notifiedAt } from './operations';

const UNITS: Readonly<Record<string, string>> = { s: 'second', min: 'minute' };

@Component({
  selector: 'ardea-operations-card',
  imports: [TranslocoPipe, LucideCircleCheck, UiButton, UiCard, UiLoader, UiNotice],
  template: `
    <ui-card [heading]="'admin.operations.title' | transloco" [attr.aria-busy]="loading()">
      @if (status(); as view) {
        <span uiCardMeta>
          {{ 'admin.operations.asOf' | transloco: { time: dates.dateTime(view.at) } }}
        </span>
      }
      @if (status(); as view) {
        <div class="warnings" role="status">
          @for (warning of view.warnings; track warning.code + (warning.subject ?? '')) {
            <ui-notice tone="error">
              {{ warning.message }}
              @if (mailed(view, warning); as at) {
                <span class="mailed">
                  {{ 'admin.operations.notified' | transloco: { time: dates.dateTime(at) } }}
                </span>
              }
            </ui-notice>
          } @empty {
            <p class="healthy">
              <svg lucideCircleCheck aria-hidden="true"></svg>
              {{ 'admin.operations.healthy' | transloco }}
            </p>
          }
        </div>
        <div class="groups">
          @for (group of groups(); track group.group) {
            <section>
              <h3>
                @if (group.label) {
                  {{ group.label | transloco }}
                } @else {
                  <span class="id">{{ group.group }}</span>
                }
              </h3>
              <dl>
                @for (row of group.rows; track row.name) {
                  <div>
                    <dt>
                      @if (row.label; as label) {
                        @if (label.subject) {
                          <span class="id">{{ label.subject }}</span> ·
                        }
                        {{ label.key | transloco }}
                      } @else {
                        <span class="id">{{ row.name }}</span>
                      }
                    </dt>
                    <dd>{{ figure(row.value, row.unit) }}</dd>
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
    .gap {
      margin-top: var(--space-md);
    }
  `,
})
export class OperationsCard {
  private readonly transloco = inject(TranslocoService);
  protected readonly dates = inject(Dates);

  readonly status = input<InstanceStatusView | null>(null);
  readonly loading = input(false);
  readonly failed = input(false);
  readonly reload = output<void>();

  protected readonly groups = computed(() => groupMetrics(this.status()?.metrics ?? []));

  protected mailed(view: InstanceStatusView, warning: StatusWarning): string | null {
    return notifiedAt(view.notified, warning);
  }

  protected figure(value: number, unit: string | null): string {
    const lang = this.transloco.getActiveLang();
    const known = unit ? UNITS[unit] : undefined;
    if (known)
      return new Intl.NumberFormat(lang, {
        style: 'unit',
        unit: known,
        maximumFractionDigits: 0,
      }).format(value);
    const figure = new Intl.NumberFormat(lang).format(value);
    return unit ? `${figure} ${unit}` : figure;
  }
}
