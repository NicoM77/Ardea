import { Component, computed, input, linkedSignal, output, signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { LucideSearch } from '@lucide/angular';
import { UiButton, UiInput } from 'ui';
import type { ComponentView } from '../../core/api/client';
import {
  ecosystems,
  filterPackages,
  NO_PACKAGE_FILTERS,
  type Ecosystem,
  type PackageFilters,
} from './component-format';

const PAGE = 50;

@Component({
  selector: 'ardea-packages-view',
  imports: [TranslocoPipe, LucideSearch, UiButton, UiInput],
  template: `
    @if (list().length) {
      <div class="tools">
        <div
          class="segments"
          role="group"
          [attr.aria-label]="'projects.packages.ecosystem' | transloco"
        >
          @for (eco of list(); track eco.id) {
            <button
              type="button"
              [attr.aria-pressed]="eco.id === current()?.id"
              (click)="choose(eco)"
            >
              {{ eco.name }} <span class="n">{{ eco.packages.length }}</span>
            </button>
          }
        </div>
        <div class="search">
          <svg lucideSearch aria-hidden="true"></svg>
          <input
            uiInput
            type="search"
            [attr.aria-label]="'projects.packages.search' | transloco"
            [placeholder]="'projects.packages.search' | transloco"
            [value]="filters().query"
            (input)="patch({ query: $any($event.target).value })"
          />
        </div>
      </div>
      <div class="filters" role="group" [attr.aria-label]="'projects.packages.filters' | transloco">
        <button
          type="button"
          class="chip"
          [attr.aria-pressed]="filters().relation === 'direct'"
          (click)="toggle('relation', 'direct')"
        >
          {{ 'projects.packages.direct' | transloco }}
        </button>
        <button
          type="button"
          class="chip"
          [attr.aria-pressed]="filters().relation === 'transitive'"
          (click)="toggle('relation', 'transitive')"
        >
          {{ 'projects.packages.transitive' | transloco }}
        </button>
        <button
          type="button"
          class="chip"
          [attr.aria-pressed]="filters().scope === 'runtime'"
          (click)="toggle('scope', 'runtime')"
        >
          {{ 'projects.packages.runtime' | transloco }}
        </button>
        <button
          type="button"
          class="chip"
          [attr.aria-pressed]="filters().scope === 'development'"
          (click)="toggle('scope', 'development')"
        >
          {{ 'projects.packages.development' | transloco }}
        </button>
        <button
          type="button"
          class="chip"
          [attr.aria-pressed]="filters().versionUnknown"
          (click)="patch({ versionUnknown: !filters().versionUnknown })"
        >
          {{ 'projects.packages.versionUnknown' | transloco }}
        </button>
      </div>
      @if (current(); as eco) {
        <p class="summary" role="status">
          {{
            'projects.packages.summary'
              | transloco
                : {
                    shown: rows().length,
                    total: eco.packages.length,
                    direct: eco.direct,
                    development: eco.development,
                  }
          }}
          @if (!eco.checkable) {
            {{ 'projects.packages.notCheckable' | transloco }}
          }
        </p>
        <table
          class="table"
          [attr.aria-label]="'projects.packages.caption' | transloco: { name: eco.name }"
        >
          <thead>
            <tr>
              <th scope="col">{{ 'projects.packages.package' | transloco }}</th>
              <th scope="col" class="end">{{ 'projects.packages.version' | transloco }}</th>
              <th scope="col">{{ 'projects.packages.relation' | transloco }}</th>
              <th scope="col">{{ 'projects.packages.use' | transloco }}</th>
              @if (detachable()) {
                <th scope="col" class="act">
                  <span class="sr-only">{{ 'projects.actions' | transloco }}</span>
                </th>
              }
            </tr>
          </thead>
          <tbody>
            @for (row of visible(); track row.id) {
              <tr>
                <td>
                  <b>{{ row.name }}</b>
                </td>
                <td class="end">
                  @if (row.version) {
                    <span class="mono">{{ row.version }}</span>
                  } @else {
                    <span class="muted">{{ 'projects.versionUnknown' | transloco }}</span>
                  }
                </td>
                <td class="wrap">{{ relationLabel(row) | transloco }}</td>
                <td class="wrap">{{ scopeLabel(row) | transloco }}</td>
                @if (detachable()) {
                  <td class="act">
                    @if (row.origin === 'repository') {
                      <button
                        uiButton
                        variant="ghost"
                        type="button"
                        [attr.aria-label]="'projects.detachNamed' | transloco: { name: row.name }"
                        (click)="detach.emit(row)"
                      >
                        {{ 'projects.detach' | transloco }}
                      </button>
                    }
                  </td>
                }
              </tr>
            } @empty {
              <tr>
                <td colspan="5" class="muted">{{ 'projects.packages.noMatch' | transloco }}</td>
              </tr>
            }
          </tbody>
        </table>
        @if (rows().length > visible().length) {
          <div>
            <button uiButton type="button" (click)="limit.set(limit() + page)">
              {{
                'projects.packages.more' | transloco: { count: rows().length - visible().length }
              }}
            </button>
          </div>
        }
      }
    }
  `,
  styles: `
    :host {
      display: grid;
      gap: var(--space-md);
      min-width: 0;
    }
    .tools {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-md);
    }
    .segments {
      display: inline-flex;
      flex-wrap: wrap;
      gap: 2px;
      padding: 2px;
      border-radius: var(--radius-card);
      background: var(--raised);
    }
    .segments button {
      height: 28px;
      padding: 0 12px;
      border: 1px solid transparent;
      border-radius: var(--radius-control);
      background: transparent;
      color: var(--muted);
      cursor: pointer;
    }
    .segments button[aria-pressed='true'] {
      border-color: var(--border);
      background: var(--surface);
      color: var(--text);
      font-weight: 500;
    }
    .segments button:focus-visible,
    .chip:focus-visible {
      outline: var(--focus-ring);
      outline-offset: var(--focus-offset);
    }
    .n {
      color: var(--faint);
      font-weight: 400;
    }
    .search {
      position: relative;
      flex: 0 1 280px;
    }
    .search svg {
      position: absolute;
      left: 10px;
      top: 50%;
      width: 16px;
      height: 16px;
      transform: translateY(-50%);
      color: var(--faint);
      pointer-events: none;
    }
    .search input {
      padding-left: 32px;
    }
    .filters {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-sm);
    }
    .chip {
      height: 28px;
      padding: 0 10px;
      border: 1px solid var(--border);
      border-radius: 999px;
      background: var(--surface);
      color: var(--muted);
      cursor: pointer;
    }
    .chip[aria-pressed='true'] {
      background: var(--active);
      border-color: var(--active);
      color: var(--text);
    }
    .summary,
    .muted {
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .table {
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
    td b {
      font-weight: 500;
      overflow-wrap: anywhere;
    }
    .end {
      text-align: right;
    }
    .act {
      width: 1%;
      padding-right: 0;
      text-align: right;
      white-space: nowrap;
    }
    .mono {
      font-family: var(--mono);
      font-size: var(--fs-sub);
    }
    @media (max-width: 640px) {
      thead {
        position: absolute;
        width: 1px;
        height: 1px;
        overflow: hidden;
        clip: rect(0 0 0 0);
      }
      .table,
      tbody,
      tr,
      td {
        display: block;
      }
      tr {
        display: grid;
        grid-template-columns: minmax(0, 1fr) auto;
        gap: 2px var(--space-md);
        padding: var(--space-sm) 0;
        border-top: 1px solid var(--rule);
      }
      td {
        padding: 0;
        border: 0;
      }
      td.end {
        grid-column: 2;
        grid-row: 1;
      }
      td.wrap {
        grid-column: 1;
        color: var(--muted);
        font-size: var(--fs-sub);
      }
      td.wrap + td.wrap {
        grid-column: 2;
        text-align: right;
      }
      td.act {
        grid-column: 2;
      }
    }
  `,
})
export class PackagesView {
  readonly components = input.required<readonly ComponentView[]>();
  readonly detachable = input(false);
  readonly detach = output<ComponentView>();

  protected readonly page = PAGE;
  protected readonly list = computed(() => ecosystems(this.components()));
  private readonly chosen = signal<string | null>(null);
  protected readonly current = computed<Ecosystem | null>(
    () => this.list().find((e) => e.id === this.chosen()) ?? this.list()[0] ?? null,
  );
  protected readonly filters = signal<PackageFilters>(NO_PACKAGE_FILTERS);
  protected readonly rows = computed(() =>
    filterPackages(this.current()?.packages ?? [], this.filters()),
  );
  protected readonly limit = linkedSignal({ source: this.rows, computation: () => PAGE });
  protected readonly visible = computed(() => this.rows().slice(0, this.limit()));

  protected choose(eco: Ecosystem): void {
    this.chosen.set(eco.id);
  }

  protected patch(change: Partial<PackageFilters>): void {
    this.filters.update((f) => ({ ...f, ...change }));
  }

  protected toggle(field: 'relation' | 'scope', value: string): void {
    this.filters.update((f) => ({ ...f, [field]: f[field] === value ? '' : value }));
  }

  protected relationLabel(row: ComponentView): string {
    return row.dependencyRelation === 'direct'
      ? 'projects.packages.direct'
      : row.dependencyRelation === 'transitive'
        ? 'projects.packages.transitive'
        : 'projects.packages.unknown';
  }

  protected scopeLabel(row: ComponentView): string {
    return row.dependencyScope === 'development'
      ? 'projects.packages.development'
      : row.dependencyScope === 'runtime'
        ? 'projects.packages.runtime'
        : 'projects.packages.unknown';
  }
}
