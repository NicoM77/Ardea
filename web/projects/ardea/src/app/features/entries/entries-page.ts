import { Component, computed, effect, inject, input, linkedSignal } from '@angular/core';
import { form, FormField, FormRoot } from '@angular/forms/signals';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { LucideSearch, LucideShieldExclamationPoint } from '@lucide/angular';
import {
  UiButton,
  UiEmptyState,
  UiField,
  UiInput,
  UiLoader,
  UiNotice,
  UiPageHeader,
  UiPill,
} from 'ui';
import { listEntriesResource, type EntrySummary } from '../../core/api/client';
import { problemMessageKey } from '../../core/api/problem';
import { Dates } from '../../core/format/dates';
import {
  EntryFilters,
  filtersFrom,
  KIND_LABELS,
  listParams,
  mergePage,
  score,
  SEVERITY_LABELS,
  signalPills,
  STATE_LABELS,
} from './entry-format';

@Component({
  selector: 'ardea-entries-page',
  imports: [
    FormField,
    FormRoot,
    RouterLink,
    TranslocoPipe,
    LucideSearch,
    LucideShieldExclamationPoint,
    UiButton,
    UiEmptyState,
    UiField,
    UiInput,
    UiLoader,
    UiNotice,
    UiPageHeader,
    UiPill,
  ],
  template: `
    <ui-page-header [heading]="'entries.title' | transloco" [sub]="'entries.sub' | transloco" />

    <form class="filters" role="search" [formRoot]="filterForm">
      <ui-field
        class="query"
        [label]="'entries.search.label' | transloco"
        [hint]="'entries.search.hint' | transloco"
      >
        <input
          uiInput
          type="search"
          spellcheck="false"
          autocomplete="off"
          [formField]="filterForm.q"
        />
      </ui-field>
      <ui-field [label]="'entries.filters.kind' | transloco">
        <select uiInput [formField]="filterForm.kind">
          <option value="">{{ 'entries.filters.anyKind' | transloco }}</option>
          <option value="vulnerability">{{ 'entries.kind.vulnerability' | transloco }}</option>
          <option value="warning">{{ 'entries.kind.warning' | transloco }}</option>
        </select>
      </ui-field>
      <ui-field [label]="'entries.filters.since' | transloco">
        <select uiInput [formField]="filterForm.since">
          <option value="">{{ 'entries.filters.anyTime' | transloco }}</option>
          <option value="day">{{ 'entries.filters.day' | transloco }}</option>
          <option value="week">{{ 'entries.filters.week' | transloco }}</option>
          <option value="month">{{ 'entries.filters.month' | transloco }}</option>
        </select>
      </ui-field>
      <button uiButton type="submit" class="go">
        <svg lucideSearch aria-hidden="true"></svg>
        {{ 'entries.search.submit' | transloco }}
      </button>
    </form>

    <section
      class="results"
      [attr.aria-busy]="page.isLoading()"
      [attr.aria-label]="'entries.results' | transloco"
    >
      <p class="count" role="status">
        @if (!page.isLoading() || rows().length) {
          {{
            'entries.shown' | transloco: { count: rows().length, more: nextCursor() ? 'yes' : 'no' }
          }}
        }
      </p>

      @if (page.error() && !rows().length) {
        <ui-notice tone="error">{{ problem() | transloco }}</ui-notice>
        <div class="gap">
          <button uiButton type="button" (click)="page.reload()">
            {{ 'entries.tryAgain' | transloco }}
          </button>
        </div>
      } @else if (page.isLoading() && !rows().length) {
        <div class="wait"><ui-loader size="page" [label]="'entries.loading' | transloco" /></div>
      } @else if (!rows().length) {
        <ui-empty-state
          [heading]="
            (filtered() ? 'entries.empty.filteredHeading' : 'entries.empty.heading') | transloco
          "
        >
          <svg uiEmptyIcon lucideShieldExclamationPoint aria-hidden="true"></svg>
          {{ (filtered() ? 'entries.empty.filteredText' : 'entries.empty.text') | transloco }}
          @if (filtered()) {
            <button uiEmptyActions uiButton type="button" (click)="clear()">
              {{ 'entries.empty.clear' | transloco }}
            </button>
          }
        </ui-empty-state>
      } @else {
        <ul class="rows">
          @for (row of rows(); track row.key) {
            <li>
              <a class="row" [routerLink]="['/entries', row.key]">
                <span class="main">
                  <span class="line">
                    <span class="id">{{ row.key }}</span>
                    @if (row.state !== 'published') {
                      <ui-pill>{{ stateLabel(row.state) | transloco }}</ui-pill>
                    }
                    @for (pill of pills(row); track pill.key) {
                      <ui-pill [tone]="pill.tone">{{ pill.key | transloco }}</ui-pill>
                    }
                  </span>
                  <span class="title">{{ row.title ?? ('entries.untitled' | transloco) }}</span>
                  <span class="sub">
                    {{ kindLabel(row.kind) | transloco }}
                    @if (aliases(row); as others) {
                      · <span class="aliases">{{ others }}</span>
                    }
                  </span>
                </span>
                <span class="side">
                  <span class="severity">
                    @if (score(row.cvssScore); as value) {
                      <b>{{ value }}</b>
                    }
                    {{ severityLabel(row.severity) | transloco }}
                  </span>
                  <time class="when" [attr.datetime]="row.changedAt">{{
                    when(row.changedAt)
                  }}</time>
                </span>
              </a>
            </li>
          }
        </ul>
        @if (page.error()) {
          <ui-notice class="gap" tone="error">{{ problem() | transloco }}</ui-notice>
        }
        @if (nextCursor()) {
          <div class="more">
            <button uiButton type="button" [loading]="page.isLoading()" (click)="loadMore()">
              {{ 'entries.more' | transloco }}
            </button>
          </div>
        }
      }
    </section>
  `,
  styles: `
    .filters {
      display: grid;
      grid-template-columns: minmax(0, 1fr) 180px 180px auto;
      align-items: start;
      gap: var(--space-md) var(--space-lg);
      margin-bottom: var(--space-lg);
    }
    .go {
      margin-top: 26px;
    }
    .count {
      min-height: var(--lh-sub);
      margin-bottom: var(--space-sm);
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .wait {
      display: grid;
      place-items: center;
      min-height: 240px;
    }
    .gap {
      display: block;
      margin-top: var(--space-md);
    }
    .rows {
      border-top: 1px solid var(--rule);
    }
    .row {
      display: grid;
      grid-template-columns: minmax(0, 1fr) auto;
      gap: var(--space-md) var(--gap);
      align-items: start;
      margin: 0 calc(-1 * var(--space-sm));
      padding: var(--space-md) var(--space-sm);
      border-bottom: 1px solid var(--rule);
      border-radius: 0;
      transition: background-color var(--t-hover) ease;
    }
    .row:focus-visible {
      outline: var(--focus-ring);
      outline-offset: -2px;
      border-radius: var(--radius-control);
    }
    .main {
      display: grid;
      gap: 2px;
      min-width: 0;
    }
    .line {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-xs) var(--space-sm);
    }
    .line .id {
      font-weight: 500;
    }
    .title {
      font-weight: 500;
      overflow-wrap: anywhere;
    }
    .sub,
    .when {
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .aliases {
      font-family: var(--mono);
      font-size: var(--fs-small);
    }
    .side {
      display: grid;
      justify-items: end;
      gap: 2px;
      text-align: right;
      white-space: nowrap;
    }
    .severity b {
      font-weight: 500;
    }
    .more {
      display: flex;
      justify-content: center;
      margin-top: var(--space-lg);
    }
    @media (hover: hover) and (pointer: fine) {
      .row:hover {
        background: var(--raised);
      }
    }
    @media (max-width: 900px) {
      .filters {
        grid-template-columns: repeat(2, minmax(0, 1fr));
      }
      .query {
        grid-column: 1 / -1;
      }
      .go {
        margin-top: 0;
        justify-self: start;
      }
    }
    @media (max-width: 640px) {
      .filters {
        grid-template-columns: minmax(0, 1fr);
      }
      .row {
        grid-template-columns: minmax(0, 1fr);
      }
      .side {
        justify-items: start;
        text-align: left;
      }
    }
  `,
})
export class EntriesPage {
  readonly q = input<string | null>(null);
  readonly kind = input<string | null>(null);
  readonly since = input<string | null>(null);

  private readonly router = inject(Router);
  private readonly dates = inject(Dates);

  protected readonly filters = computed(() =>
    filtersFrom({ q: this.q(), kind: this.kind(), since: this.since() }),
  );
  protected readonly filtered = computed(() => {
    const f = this.filters();
    return !!(f.q || f.kind || f.since);
  });

  private readonly cursor = linkedSignal<EntryFilters, string | undefined>({
    source: this.filters,
    computation: () => undefined,
  });
  private readonly params = computed(() => listParams(this.filters(), this.cursor(), new Date()));
  protected readonly page = listEntriesResource(this.params);

  protected readonly rows = linkedSignal<
    { filters: EntryFilters; cursor: string | undefined; items: EntrySummary[] | undefined },
    EntrySummary[]
  >({
    source: () => ({
      filters: this.filters(),
      cursor: this.cursor(),
      items: this.page.hasValue() ? this.page.value()?.items : undefined,
    }),
    computation: (source, previous) => {
      const shown = previous && previous.source.filters === source.filters ? previous.value : [];
      return source.items ? mergePage(shown, source.items, !source.cursor) : shown;
    },
  });
  protected readonly nextCursor = computed(() =>
    this.page.hasValue() ? (this.page.value()?.nextCursor ?? null) : null,
  );
  protected readonly problem = computed(() =>
    problemMessageKey(this.page.error(), { fallback: 'entries.loadFailed' }),
  );

  private readonly model = linkedSignal(() => ({ ...this.filters() }));
  protected readonly filterForm = form(this.model, {
    submission: {
      action: async () => {
        await this.apply(this.model());
        return undefined;
      },
    },
  });

  constructor() {
    let lastChoice = { kind: '', since: '' };
    effect(() => {
      const { kind, since } = this.model();
      const current = this.filters();
      const previous = lastChoice;
      lastChoice = { kind, since };
      if (
        (kind !== previous.kind || since !== previous.since) &&
        (kind !== current.kind || since !== current.since)
      ) {
        void this.apply({
          ...current,
          kind: kind as EntryFilters['kind'],
          since: since as EntryFilters['since'],
        });
      }
    });
  }

  protected loadMore(): void {
    const next = this.nextCursor();
    if (next) this.cursor.set(next);
  }

  protected clear(): void {
    void this.apply({ q: '', kind: '', since: '' });
  }

  private apply(filters: EntryFilters): Promise<boolean> {
    return this.router.navigate([], {
      queryParams: {
        q: filters.q.trim() || null,
        kind: filters.kind || null,
        since: filters.since || null,
      },
      replaceUrl: true,
    });
  }

  protected readonly score = score;
  protected readonly pills = signalPills;

  protected severityLabel(severity: string): string {
    return SEVERITY_LABELS[severity] ?? 'entries.severity.unrated';
  }

  protected stateLabel(state: string): string {
    return STATE_LABELS[state] ?? 'entries.state.published';
  }

  protected kindLabel(kind: string): string {
    return KIND_LABELS[kind] ?? 'entries.kind.vulnerability';
  }

  protected aliases(entry: EntrySummary): string {
    const others = entry.aliases.filter((alias) => alias !== entry.key);
    return others.slice(0, 3).join(', ') + (others.length > 3 ? ' …' : '');
  }

  protected when(value: string): string {
    return this.dates.dateTime(value);
  }
}
