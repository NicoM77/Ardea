import { Component, computed, inject, input, linkedSignal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { LucideListChecks, LucideSearch } from '@lucide/angular';
import { UiButton, UiEmptyState, UiInput, UiLoader, UiNotice, UiPriority } from 'ui';
import {
  listActionsResource,
  listProjectsResource,
  type ActionSummary,
} from '../../core/api/client';
import { problemMessageKey } from '../../core/api/problem';
import { reloadOnLiveChange } from '../../core/live/live-events';
import { injectOrganization } from '../organizations/current-organization';
import { ACTION_PRIORITIES, countByPriority, num, type ActionPriority } from './action-format';
import { ActionsTable } from './actions-table';
import {
  actionCountParams,
  actionFiltersFrom,
  actionFiltersQuery,
  actionListParams,
  isFiltered,
  togglePriority,
  type ActionFilters,
} from './list-filters';

function mergeActions(
  shown: readonly ActionSummary[],
  page: readonly ActionSummary[],
  isFirstPage: boolean,
): ActionSummary[] {
  if (isFirstPage) return [...page];
  const known = new Set(shown.map((a) => num(a.number)));
  return [...shown, ...page.filter((a) => !known.has(num(a.number)))];
}

@Component({
  selector: 'ardea-actions-page',
  imports: [
    RouterLink,
    TranslocoPipe,
    LucideListChecks,
    LucideSearch,
    UiButton,
    UiEmptyState,
    UiInput,
    UiLoader,
    UiNotice,
    UiPriority,
    ActionsTable,
  ],
  template: `
    <header class="head">
      <div>
        <h1>{{ 'actions.title' | transloco }}</h1>
        <p class="summary" role="status">
          @if (summary(); as s) {
            {{ s.key | transloco: s.params }}
          }
        </p>
      </div>
      <form class="finders" role="search" (submit)="search($event, query.value)">
        <label>
          <span class="sr-only">{{ 'actions.filters.project' | transloco }}</span>
          <select uiInput (change)="setProject(projectSelect.value)" #projectSelect>
            <option value="" [selected]="!filters().project">
              {{ 'actions.filters.allProjects' | transloco }}
            </option>
            @for (p of projectOptions(); track p.id) {
              <option [value]="p.id" [selected]="p.id === filters().project">{{ p.name }}</option>
            }
          </select>
        </label>
        <label class="query">
          <span class="sr-only">{{ 'actions.filters.search' | transloco }}</span>
          <svg lucideSearch aria-hidden="true"></svg>
          <input
            uiInput
            type="search"
            autocomplete="off"
            spellcheck="false"
            [value]="filters().q"
            [placeholder]="'actions.filters.searchHint' | transloco"
            #query
          />
        </label>
      </form>
    </header>

    <div class="list">
      <nav class="tabs" [attr.aria-label]="'actions.filters.view' | transloco">
        <a
          routerLink="."
          [queryParams]="viewQuery('open')"
          [attr.aria-current]="filters().view === 'open' ? 'page' : null"
          >{{ 'actions.view.open' | transloco }}</a
        >
        <a
          routerLink="."
          [queryParams]="viewQuery('closed')"
          [attr.aria-current]="filters().view === 'closed' ? 'page' : null"
          >{{ 'actions.view.closed' | transloco }}</a
        >
      </nav>

      <div class="chips" role="group" [attr.aria-label]="'actions.filters.priority' | transloco">
        @for (p of priorities; track p) {
          <button
            type="button"
            class="chip"
            [attr.aria-pressed]="filters().priorities.includes(p)"
            (click)="toggle(p)"
          >
            <ui-priority [level]="p" symbolOnly aria-hidden="true" />
            {{ priorityLabels[p] | transloco }}
            @if (chipCounts(); as counts) {
              <b>{{ counts[p] }}</b>
            }
          </button>
        }
      </div>

      <section [attr.aria-busy]="page.isLoading()" [attr.aria-label]="caption()">
        @if (page.error() && !rows().length) {
          <ui-notice tone="error">{{ problem() | transloco }}</ui-notice>
          <button uiButton type="button" class="gap" (click)="page.reload()">
            {{ 'actions.tryAgain' | transloco }}
          </button>
        } @else if (page.isLoading() && !rows().length) {
          <div class="wait">
            <ui-loader size="page" [label]="'actions.loading' | transloco" />
          </div>
        } @else if (!rows().length) {
          <ui-empty-state [heading]="emptyHeading() | transloco">
            <svg uiEmptyIcon lucideListChecks aria-hidden="true"></svg>
            {{ emptyText() | transloco }}
            @if (filtered()) {
              <button uiEmptyActions uiButton type="button" (click)="clear()">
                {{ 'actions.empty.clear' | transloco }}
              </button>
            }
          </ui-empty-state>
        } @else {
          <ardea-actions-table
            [rows]="rows()"
            [closed]="filters().view === 'closed'"
            [caption]="caption()"
          />
          @if (nextCursor()) {
            <div class="more">
              <button uiButton type="button" [loading]="page.isLoading()" (click)="loadMore()">
                {{ 'actions.more' | transloco }}
              </button>
            </div>
          }
        }
      </section>
    </div>
  `,
  styles: `
    .head {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-end;
      justify-content: space-between;
      gap: var(--space-lg) var(--gap);
      margin-bottom: var(--space-xl);
    }
    .summary {
      min-height: var(--lh-body);
      margin-top: var(--space-xs);
      color: var(--muted);
    }
    .finders {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-sm);
    }
    .finders select {
      width: auto;
      min-width: 160px;
    }
    .query {
      position: relative;
      display: block;
      width: 260px;
    }
    .query svg {
      position: absolute;
      left: 10px;
      top: 50%;
      margin-top: -8px;
      color: var(--faint);
      pointer-events: none;
    }
    .query input {
      padding-left: 32px;
    }
    .tabs {
      display: flex;
      gap: var(--gap);
      border-bottom: 1px solid var(--border);
    }
    .tabs a {
      display: inline-flex;
      align-items: center;
      height: 40px;
      margin-bottom: -1px;
      border-bottom: 2px solid transparent;
      color: var(--muted);
    }
    .tabs a[aria-current='page'] {
      border-bottom-color: var(--text);
      color: var(--text);
      font-weight: 500;
    }
    .chips {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-sm);
      margin: var(--space-lg) 0;
    }
    .chip {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      height: var(--control-height);
      padding: 0 10px;
      border: 1px solid var(--control);
      border-radius: var(--radius-pill);
      background: var(--surface);
      color: var(--muted);
      cursor: pointer;
      transition:
        background-color var(--t-hover) ease,
        transform var(--t-press) var(--ease-out);
    }
    .chip:active {
      transform: scale(0.97);
    }
    .chip b {
      color: var(--text);
      font-weight: 500;
    }
    .chip[aria-pressed='true'] {
      border-color: var(--active);
      background: var(--active);
      color: var(--text);
    }
    .wait {
      display: grid;
      place-items: center;
      min-height: 240px;
    }
    .gap {
      margin-top: var(--space-md);
    }
    .more {
      display: flex;
      justify-content: center;
      margin-top: var(--space-lg);
    }
    @media (hover: hover) and (pointer: fine) {
      .tabs a:hover,
      .chip:hover {
        color: var(--text);
      }
    }
    @media (max-width: 640px) {
      .finders,
      .query,
      .finders label {
        width: 100%;
      }
    }
  `,
})
export class ActionsPage {
  readonly view = input<string | null>(null);
  readonly priority = input<string | null>(null);
  readonly project = input<string | null>(null);
  readonly q = input<string | null>(null);

  private readonly router = inject(Router);
  private readonly transloco = inject(TranslocoService);
  private readonly organization = injectOrganization().organization;
  private readonly orgId = computed(() => this.organization()?.id);

  protected readonly priorities = ACTION_PRIORITIES;
  protected readonly priorityLabels: Record<ActionPriority, string> = {
    critical: 'ui.priority.critical',
    high: 'ui.priority.high',
    medium: 'ui.priority.medium',
    low: 'ui.priority.low',
  };

  protected readonly filters = computed(() =>
    actionFiltersFrom({
      view: this.view(),
      priority: this.priority(),
      project: this.project(),
      q: this.q(),
    }),
  );
  protected readonly filtered = computed(() => isFiltered(this.filters()));

  private readonly cursor = linkedSignal<ActionFilters, string | undefined>({
    source: this.filters,
    computation: () => undefined,
  });
  protected readonly page = listActionsResource(
    this.orgId,
    computed(() => actionListParams(this.filters(), this.cursor())),
  );
  protected readonly rows = linkedSignal<
    { filters: ActionFilters; cursor: string | undefined; items: ActionSummary[] | undefined },
    ActionSummary[]
  >({
    source: () => ({
      filters: this.filters(),
      cursor: this.cursor(),
      items: this.page.hasValue() ? this.page.value()?.items : undefined,
    }),
    computation: (source, previous) => {
      const shown = previous && previous.source.filters === source.filters ? previous.value : [];
      return source.items ? mergeActions(shown, source.items, !source.cursor) : shown;
    },
  });
  protected readonly nextCursor = computed(() =>
    this.page.hasValue() ? (this.page.value()?.nextCursor ?? null) : null,
  );
  protected readonly problem = computed(() =>
    problemMessageKey(this.page.error(), { fallback: 'actions.loadFailed' }),
  );

  private readonly counts = listActionsResource(
    this.orgId,
    computed(() => actionCountParams(this.filters())),
  );
  private readonly countRows = computed(() =>
    this.counts.hasValue() ? (this.counts.value()?.items ?? []) : null,
  );
  private readonly countsComplete = computed(
    () => this.counts.hasValue() && !this.counts.value()?.nextCursor,
  );
  protected readonly chipCounts = computed(() => {
    const rows = this.countRows();
    return rows && this.countsComplete() ? countByPriority(rows) : null;
  });
  protected readonly inView = computed(() => {
    const wanted = this.filters().priorities as string[];
    const rows = this.countRows() ?? [];
    return wanted.length ? rows.filter((r) => wanted.includes(r.levelName)) : rows;
  });

  private readonly projects = listProjectsResource(this.orgId);
  protected readonly projectOptions = computed(() =>
    this.projects.hasValue() ? (this.projects.value() ?? []).filter((p) => !p.archived) : [],
  );

  protected readonly summary = computed(() => {
    const rows = this.countRows();
    if (!rows) return null;
    const f = this.filters();
    const more = this.countsComplete() ? 'no' : 'yes';
    if (this.filtered())
      return {
        key: f.view === 'open' ? 'actions.summary.matchOpen' : 'actions.summary.matchClosed',
        params: { count: this.inView().length, more },
      };
    if (f.view === 'closed')
      return { key: 'actions.summary.closed', params: { count: rows.length, more } };
    return {
      key: 'actions.summary.open',
      params: {
        count: rows.length,
        more,
        critical: rows.filter((r) => r.levelName === 'critical').length,
      },
    };
  });

  protected readonly caption = computed(() =>
    this.transloco.translate(
      this.filters().view === 'open' ? 'actions.caption.open' : 'actions.caption.closed',
    ),
  );
  protected readonly emptyHeading = computed(() =>
    this.filtered()
      ? 'actions.empty.filteredHeading'
      : this.filters().view === 'open'
        ? 'areas.actions.emptyHeading'
        : 'actions.empty.closedHeading',
  );
  protected readonly emptyText = computed(() =>
    this.filtered()
      ? 'actions.empty.filteredText'
      : this.filters().view === 'open'
        ? 'areas.actions.emptyText'
        : 'actions.empty.closedText',
  );

  constructor() {
    reloadOnLiveChange(
      {
        reload: () => {
          if (!this.cursor()) return this.page.reload();
          this.cursor.set(undefined);
          return true;
        },
      },
      this.counts,
    );
  }

  protected viewQuery(view: 'open' | 'closed'): Record<string, string | null> {
    return actionFiltersQuery({ ...this.filters(), view });
  }

  protected toggle(priority: ActionPriority): void {
    void this.apply(togglePriority(this.filters(), priority));
  }

  protected setProject(project: string): void {
    void this.apply({ ...this.filters(), project });
  }

  protected search(event: Event, q: string): void {
    event.preventDefault();
    void this.apply({ ...this.filters(), q: q.trim() });
  }

  protected clear(): void {
    void this.apply({ view: this.filters().view, priorities: [], project: '', q: '' });
  }

  protected loadMore(): void {
    const next = this.nextCursor();
    if (next) this.cursor.set(next);
  }

  private apply(filters: ActionFilters): Promise<boolean> {
    return this.router.navigate([], { queryParams: actionFiltersQuery(filters), replaceUrl: true });
  }
}
