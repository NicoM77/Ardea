import { Component, computed, DestroyRef, inject, input, output, signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { LucidePlus, LucideSearch } from '@lucide/angular';
import { UiButton, UiInput, UiLoader } from 'ui';
import {
  searchCatalogResource,
  type CatalogProductView,
  type SearchCatalogParams,
} from '../../core/api/client';
import { CATALOG_GROUPS, KIND_LABELS } from './component-format';

export type Picked = { product: CatalogProductView } | { name: string; kind: string };

@Component({
  selector: 'ardea-catalog-picker',
  imports: [TranslocoPipe, LucidePlus, LucideSearch, UiButton, UiInput, UiLoader],
  template: `
    @if (!kinds()) {
      <div class="groups" role="group" [attr.aria-label]="'projects.picker.groups' | transloco">
        @for (group of groups; track group.id) {
          <button
            type="button"
            class="chip"
            [attr.aria-pressed]="group.id === groupId()"
            (click)="groupId.set(group.id === groupId() ? null : group.id)"
          >
            {{ group.label | transloco }}
          </button>
        }
      </div>
    }
    <div class="search">
      <svg lucideSearch aria-hidden="true"></svg>
      <input
        uiInput
        type="search"
        autocomplete="off"
        spellcheck="false"
        [attr.aria-label]="label() || ('projects.picker.label' | transloco)"
        [placeholder]="placeholder() || ('projects.picker.placeholder' | transloco)"
        [value]="text()"
        (input)="type($event)"
      />
    </div>

    @if (query()) {
      <div class="results" [attr.aria-busy]="results.isLoading()">
        @if (shown(); as list) {
          @for (product of list; track product.key) {
            <div class="row">
              <span class="what">
                <b>{{ product.name }}</b>
                <small
                  >{{ kindLabels[product.kind] ?? product.kind | transloco
                  }}{{ product.vendor ? ' · ' + product.vendor : '' }}</small
                >
              </span>
              <button
                uiButton
                type="button"
                [attr.aria-label]="'projects.picker.addNamed' | transloco: { name: product.name }"
                (click)="pick({ product })"
              >
                {{ 'projects.picker.add' | transloco }}
              </button>
            </div>
          } @empty {
            <p class="row muted" role="status">
              {{ 'projects.picker.none' | transloco: { query: query() } }}
            </p>
          }
        } @else if (results.error()) {
          <p class="row muted">{{ 'projects.picker.failed' | transloco }}</p>
        } @else {
          <div class="row"><ui-loader [label]="'projects.picker.searching' | transloco" /></div>
        }
        <div class="row free">
          <span class="what">
            <b>{{ 'projects.picker.freeTitle' | transloco }}</b>
            <small>{{ 'projects.picker.freeText' | transloco: { name: query() } }}</small>
          </span>
          <span class="free-actions">
            <select
              uiInput
              [attr.aria-label]="'projects.picker.freeKind' | transloco"
              [value]="freeKind()"
              (change)="freeKind.set($any($event.target).value)"
            >
              @for (kind of freeKinds(); track kind) {
                <option [value]="kind" [selected]="kind === freeKind()">
                  {{ kindLabels[kind] | transloco }}
                </option>
              }
            </select>
            <button uiButton variant="ghost" type="button" (click)="pickFree()">
              <svg lucidePlus aria-hidden="true"></svg>
              {{ 'projects.picker.addFree' | transloco }}
            </button>
          </span>
        </div>
      </div>
    }
  `,
  styles: `
    :host {
      display: grid;
      gap: var(--space-sm);
      min-width: 0;
    }
    .groups {
      display: flex;
      flex-wrap: wrap;
      gap: 6px;
    }
    .chip {
      height: 28px;
      padding: 0 10px;
      border: 1px solid var(--border);
      border-radius: 999px;
      background: var(--surface);
      color: var(--muted);
      font-size: var(--fs-sub);
      cursor: pointer;
      transition:
        background-color var(--t-hover) ease,
        color var(--t-hover) ease;
    }
    .chip[aria-pressed='true'] {
      background: var(--active);
      border-color: var(--active);
      color: var(--text);
    }
    .chip:focus-visible {
      outline: var(--focus-ring);
      outline-offset: var(--focus-offset);
    }
    .search {
      position: relative;
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
    .results {
      display: grid;
      border: 1px solid var(--border);
      border-radius: var(--radius-card);
    }
    .row {
      display: grid;
      grid-template-columns: minmax(0, 1fr) auto;
      gap: var(--space-md);
      align-items: center;
      min-height: 48px;
      padding: var(--space-sm) var(--space-md);
    }
    .row + .row {
      border-top: 1px solid var(--rule);
    }
    .what {
      display: grid;
      min-width: 0;
    }
    .what b {
      font-weight: 500;
    }
    small,
    .muted {
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .free-actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-sm);
      justify-content: flex-end;
    }
    .free-actions select {
      width: auto;
    }
    @media (max-width: 640px) {
      .row.free {
        grid-template-columns: minmax(0, 1fr);
      }
      .free-actions {
        justify-content: flex-start;
      }
    }
    @media (hover: hover) and (pointer: fine) {
      .chip:hover {
        color: var(--text);
        border-color: var(--control);
      }
    }
  `,
})
export class CatalogPicker {
  readonly kinds = input<readonly string[] | null>(null);
  readonly label = input<string | null>(null);
  readonly placeholder = input<string | null>(null);
  readonly picked = output<Picked>();

  protected readonly groups = CATALOG_GROUPS;
  protected readonly kindLabels = KIND_LABELS;
  protected readonly groupId = signal<string | null>(null);
  protected readonly text = signal('');
  protected readonly query = signal('');
  protected readonly freeKind = signal('other');

  private readonly params = computed<SearchCatalogParams | undefined>(() =>
    this.query() ? { q: this.query(), limit: 20 } : undefined,
  );
  protected readonly results = searchCatalogResource(this.params);

  private readonly allowedKinds = computed<readonly string[] | null>(() => {
    if (this.kinds()) return this.kinds();
    const group = CATALOG_GROUPS.find((g) => g.id === this.groupId());
    return group ? group.kinds : null;
  });
  protected readonly freeKinds = computed(() => this.kinds() ?? Object.keys(KIND_LABELS));
  protected readonly shown = computed(() => {
    if (!this.results.hasValue()) return null;
    const kinds = this.allowedKinds();
    return (this.results.value() ?? []).filter((p) => !kinds || kinds.includes(p.kind));
  });

  private timer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.timer));
  }

  protected type(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.text.set(value);
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.query.set(value.trim()), 250);
    if (!value.trim()) this.query.set('');
  }

  protected pick(choice: Picked): void {
    this.picked.emit(choice);
    this.clear();
  }

  protected pickFree(): void {
    const name = this.query();
    if (!name) return;
    const kinds = this.freeKinds();
    this.pick({ name, kind: kinds.includes(this.freeKind()) ? this.freeKind() : kinds[0] });
  }

  private clear(): void {
    clearTimeout(this.timer);
    this.text.set('');
    this.query.set('');
  }
}
