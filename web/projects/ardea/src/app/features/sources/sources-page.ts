import { Component, computed, inject, input, signal } from '@angular/core';
import { Router } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { LucideChevronDown, LucideRadioTower } from '@lucide/angular';
import { UiButton, UiEmptyState, UiField, UiInput, UiLoader, UiNotice, UiPageHeader } from 'ui';
import { listSourcesResource, type SourceStatus } from '../../core/api/client';
import { problemMessageKey } from '../../core/api/problem';
import { Session } from '../../core/session/session';
import { CatalogLearning } from './catalog-learning';
import { ReviewList } from './review-list';
import { SourceAdmin } from './source-admin';
import {
  count,
  filterOptions,
  filterSources,
  healthCounts,
  interval,
  OPERATOR_KIND_LABELS,
} from './source-format';
import { SourceHealth } from './source-health';

@Component({
  selector: 'ardea-sources-page',
  imports: [
    TranslocoPipe,
    CatalogLearning,
    LucideChevronDown,
    LucideRadioTower,
    ReviewList,
    SourceAdmin,
    SourceHealth,
    UiButton,
    UiEmptyState,
    UiField,
    UiInput,
    UiLoader,
    UiNotice,
    UiPageHeader,
  ],
  template: `
    <ui-page-header [heading]="'sources.title' | transloco" [sub]="'sources.sub' | transloco" />

    @if (sources.hasValue() && sources.value(); as all) {
      @if (all.length) {
        <p class="summary" role="status">
          {{
            'sources.summary'
              | transloco
                : {
                    total: all.length,
                    failing: health().failing,
                    degraded: health().degraded,
                    disabled: health().disabled,
                  }
          }}
        </p>

        <div class="filters" role="group" [attr.aria-label]="'sources.filters.label' | transloco">
          <ui-field [label]="'sources.filters.operatorKind' | transloco">
            <select uiInput [value]="operatorKind()" (change)="choose('operatorKind', $event)">
              <option value="">{{ 'sources.filters.anyKind' | transloco }}</option>
              @for (kind of options().kinds; track kind) {
                <option [value]="kind">{{ kindLabel(kind) | transloco }}</option>
              }
            </select>
          </ui-field>
          <ui-field [label]="'sources.filters.operator' | transloco">
            <select uiInput [value]="operator()" (change)="choose('operator', $event)">
              <option value="">{{ 'sources.filters.anyOperator' | transloco }}</option>
              @for (name of options().operators; track name) {
                <option [value]="name">{{ name }}</option>
              }
            </select>
          </ui-field>
        </div>

        @if (shown().length) {
          <ul class="sources">
            @for (source of shown(); track source.key) {
              <li class="source" [class.is-open]="open() === source.key">
                <div class="head">
                  <div class="name">
                    <h2>{{ source.name }}</h2>
                    <span class="sub">
                      {{ source.operator }} · {{ kindLabel(source.operatorKind) | transloco }}
                    </span>
                  </div>
                  <div class="state">
                    <ardea-source-health [health]="source.health" />
                    @if (source.healthReason) {
                      <span class="sub">{{ source.healthReason }}</span>
                    }
                  </div>
                </div>

                <dl class="metrics">
                  <div>
                    <dt>{{ 'sources.lastSuccess' | transloco }}</dt>
                    <dd>
                      {{
                        source.lastSuccessAt
                          ? when(source.lastSuccessAt)
                          : ('sources.never' | transloco)
                      }}
                    </dd>
                  </div>
                  <div>
                    <dt>{{ 'sources.lastAttempt' | transloco }}</dt>
                    <dd>
                      {{
                        source.lastAttemptAt
                          ? when(source.lastAttemptAt)
                          : ('sources.never' | transloco)
                      }}
                    </dd>
                  </div>
                  <div>
                    <dt>{{ 'sources.nextRun' | transloco }}</dt>
                    <dd>
                      @if (source.enabled) {
                        {{ when(source.nextSyncAt) }}
                        <span class="unit">
                          @let every = intervalOf(source);
                          {{ every.key | transloco: { count: every.value } }}
                        </span>
                      } @else {
                        {{ 'sources.paused' | transloco }}
                      }
                    </dd>
                  </div>
                  <div>
                    <dt>{{ 'sources.records' | transloco }}</dt>
                    <dd>
                      {{ number(source.records) }}
                      @if (number(source.quarantined)) {
                        <span class="unit">{{
                          'sources.quarantined' | transloco: { count: number(source.quarantined) }
                        }}</span>
                      }
                    </dd>
                  </div>
                  @if (number(source.consecutiveFailures)) {
                    <div>
                      <dt>{{ 'sources.failures' | transloco }}</dt>
                      <dd>{{ number(source.consecutiveFailures) }}</dd>
                    </div>
                  }
                </dl>

                <p class="attribution">{{ source.attribution }}</p>

                @if (isAdmin()) {
                  <button
                    uiButton
                    variant="ghost"
                    type="button"
                    class="toggle"
                    [attr.aria-expanded]="open() === source.key"
                    [attr.aria-controls]="'source-' + source.key"
                    (click)="open.set(open() === source.key ? null : source.key)"
                  >
                    <svg lucideChevronDown aria-hidden="true"></svg>
                    {{ 'sources.admin.open' | transloco }}
                  </button>
                  @if (open() === source.key) {
                    <ardea-source-admin
                      class="admin"
                      [id]="'source-' + source.key"
                      [source]="source"
                      (changed)="sources.reload()"
                    />
                  }
                }
              </li>
            }
          </ul>
        } @else {
          <ui-empty-state [heading]="'sources.empty.filteredHeading' | transloco">
            {{ 'sources.empty.filteredText' | transloco }}
            <button uiEmptyActions uiButton type="button" (click)="clear()">
              {{ 'sources.empty.clear' | transloco }}
            </button>
          </ui-empty-state>
        }
      } @else {
        <ui-empty-state [heading]="'areas.sources.emptyHeading' | transloco">
          <svg uiEmptyIcon lucideRadioTower aria-hidden="true"></svg>
          {{ 'areas.sources.emptyText' | transloco }}
        </ui-empty-state>
      }
    } @else if (sources.error()) {
      <ui-notice tone="error">{{ problem() | transloco }}</ui-notice>
      <div class="gap">
        <button uiButton type="button" (click)="sources.reload()">
          {{ 'entries.tryAgain' | transloco }}
        </button>
      </div>
    } @else {
      <div class="wait" aria-busy="true">
        <ui-loader size="page" [label]="'sources.loading' | transloco" />
      </div>
    }

    @if (isAdmin()) {
      <ardea-review-list class="review" />
      <ardea-catalog-learning class="review" id="catalogue" [rawKey]="learned()" />
    }
  `,
  styles: `
    .summary {
      margin: calc(-1 * var(--space-sm)) 0 var(--space-lg);
      color: var(--muted);
    }
    .filters {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 260px));
      gap: var(--space-lg);
      margin-bottom: var(--gap);
    }
    .wait {
      display: grid;
      place-items: center;
      min-height: 280px;
    }
    .gap {
      margin-top: var(--space-md);
    }
    .sources {
      display: grid;
      gap: var(--space-lg);
    }
    .source {
      display: grid;
      gap: var(--space-md);
      padding: var(--card-pad);
      border: 1px solid var(--border);
      border-radius: var(--radius-card);
      background: var(--surface);
    }
    .head {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-start;
      justify-content: space-between;
      gap: var(--space-sm) var(--gap);
    }
    .name,
    .state {
      display: grid;
      gap: 2px;
      min-width: 0;
    }
    .state {
      justify-items: end;
      text-align: right;
    }
    .sub,
    .unit {
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .unit {
      display: block;
    }
    .metrics {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(170px, 1fr));
      gap: var(--space-md) var(--gap);
    }
    .metrics dt {
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .metrics dd {
      margin-top: 2px;
      font-weight: 500;
    }
    .attribution {
      color: var(--faint);
      font-size: var(--fs-small);
      line-height: var(--lh-small);
    }
    .toggle {
      justify-self: start;
    }
    .toggle svg {
      transition: transform var(--t-ui) var(--ease-out);
    }
    .is-open .toggle svg {
      transform: rotate(180deg);
    }
    .review {
      display: block;
      margin-top: var(--gap);
    }
    .admin {
      padding-top: var(--space-md);
      border-top: 1px solid var(--rule);
    }
    @media (max-width: 640px) {
      .filters {
        grid-template-columns: minmax(0, 1fr);
      }
      .state {
        justify-items: start;
        text-align: left;
      }
    }
  `,
})
export class SourcesPage {
  readonly operatorKind = input<string>('');
  readonly operator = input<string>('');
  readonly learned = input<string>('');

  private readonly router = inject(Router);
  private readonly transloco = inject(TranslocoService);
  private readonly session = inject(Session);

  protected readonly sources = listSourcesResource();
  protected readonly isAdmin = computed(() => this.session.user()?.isInstanceAdmin === true);
  protected readonly open = signal<string | null>(null);

  private readonly all = computed<SourceStatus[]>(() =>
    this.sources.hasValue() ? (this.sources.value() ?? []) : [],
  );
  protected readonly options = computed(() => filterOptions(this.all()));
  protected readonly shown = computed(() =>
    filterSources(this.all(), {
      operatorKind: this.operatorKind() ?? '',
      operator: this.operator() ?? '',
    }),
  );
  protected readonly health = computed(() => healthCounts(this.all()));
  protected readonly problem = computed(() =>
    problemMessageKey(this.sources.error(), { fallback: 'sources.loadFailed' }),
  );

  protected readonly number = count;

  protected choose(filter: 'operatorKind' | 'operator', event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    void this.router.navigate([], {
      queryParams: { [filter]: value || null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  protected clear(): void {
    void this.router.navigate([], { queryParams: {}, replaceUrl: true });
  }

  protected kindLabel(kind: string): string {
    return OPERATOR_KIND_LABELS[kind] ?? 'sources.operatorKind.community';
  }

  protected intervalOf(source: SourceStatus): { key: string; value: number } {
    return interval(count(source.intervalMinutes));
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
