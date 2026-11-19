import {
  Component,
  computed,
  DestroyRef,
  ElementRef,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { LucideSearch } from '@lucide/angular';
import { UiInput, UiLoader } from 'ui';
import {
  searchCatalogResource,
  type CatalogProductView,
  type SearchCatalogParams,
} from '../../core/api/client';
import { KIND_LABELS } from '../projects/component-format';
import { searchHint } from './stack-format';
import { StackStore } from './stack-store';

let nextAdd = 0;

/**
 * Adds a product to the stack from the catalogue (master plan 17.2): a combobox over the catalogue
 * search, which knows names, aliases and the odd typo (`GET /catalog/products`). The focus stays in
 * the field; the arrow keys move through the products, Enter adds one, Escape closes the list.
 * Only catalogue products: a free name would never match a source, so it would raise nothing.
 */
@Component({
  selector: 'ardea-stack-add',
  imports: [TranslocoPipe, LucideSearch, UiInput, UiLoader],
  template: `
    <label class="label" [for]="id">{{ 'stack.add.label' | transloco }}</label>
    <div class="search">
      <svg lucideSearch aria-hidden="true"></svg>
      <input
        #box
        uiInput
        type="text"
        role="combobox"
        autocomplete="off"
        spellcheck="false"
        aria-autocomplete="list"
        [id]="id"
        [attr.aria-expanded]="listed()"
        [attr.aria-controls]="id + '-list'"
        [attr.aria-activedescendant]="activeId()"
        [attr.aria-describedby]="id + '-hint'"
        [placeholder]="'stack.add.placeholder' | transloco"
        [value]="text()"
        (input)="type(box.value)"
        (keydown)="key($event)"
        (blur)="closeSoon()"
        (focus)="reopen()"
      />
    </div>
    <p class="hint" [id]="id + '-hint'">{{ 'stack.add.hint' | transloco }}</p>

    <!-- The list stays in the page while it is closed, so aria-controls always points somewhere. -->
    <div
      class="list"
      role="listbox"
      [id]="id + '-list'"
      [hidden]="!listed()"
      [attr.aria-label]="'stack.add.results' | transloco"
      (mousedown)="$event.preventDefault()"
    >
      @for (product of options(); track product.key; let i = $index) {
        <!-- Options of a combobox: the focus stays in the field, the keyboard moves aria-activedescendant -->
        <!-- eslint-disable-next-line @angular-eslint/template/click-events-have-key-events, @angular-eslint/template/interactive-supports-focus -->
        <div
          role="option"
          class="option"
          [id]="id + '-o-' + i"
          [attr.aria-selected]="i === active()"
          (click)="pick(product)"
          (pointermove)="active.set(i)"
        >
          <span>{{ product.name }}</span>
          <small>{{ kindLabels[product.kind] ?? kindLabels['other'] | transloco }}</small>
        </div>
      }
    </div>
    @if (open()) {
      <div class="notes" role="status">
        @if (results.isLoading() && !options().length) {
          <ui-loader [label]="'stack.add.searching' | transloco" />
        } @else if (results.error()) {
          <p>{{ 'stack.add.failed' | transloco }}</p>
        } @else if (!options().length && !note()) {
          <p>{{ 'stack.add.none' | transloco: { query: query() } }}</p>
        }
        @if (note(); as note) {
          <p>{{ note | transloco }}</p>
        }
      </div>
    }
  `,
  styles: `
    :host {
      display: grid;
      gap: 6px;
      min-width: 0;
    }
    .label {
      font-weight: 500;
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
    .hint {
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .list:not([hidden]) {
      display: grid;
      max-height: 300px;
      overflow-y: auto;
      padding: 4px;
      border: 1px solid var(--border);
      border-radius: var(--radius-card);
      background: var(--surface);
    }
    .option {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-md);
      min-height: 40px;
      padding: 0 var(--space-sm);
      border-radius: var(--radius-control);
      cursor: pointer;
    }
    .option small,
    .notes {
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .option[aria-selected='true'] {
      background: var(--active);
    }
    .notes {
      display: grid;
      gap: 4px;
    }
  `,
})
export class StackAdd {
  private readonly store = inject(StackStore);
  private readonly box = viewChild.required<ElementRef<HTMLInputElement>>('box');

  protected readonly id = `stack-add-${++nextAdd}`;
  protected readonly kindLabels = KIND_LABELS;
  protected readonly text = signal('');
  protected readonly query = signal('');
  protected readonly active = signal(0);
  private readonly expanded = signal(false);

  private readonly params = computed<SearchCatalogParams | undefined>(() =>
    this.query() ? { q: this.query(), limit: 12 } : undefined,
  );
  protected readonly results = searchCatalogResource(this.params);
  /** Products the stack does not name yet; a stack entry stands for the product itself (17.2). */
  protected readonly options = computed<CatalogProductView[]>(() => {
    if (!this.results.hasValue() || !this.query()) return [];
    const taken = this.store.keys();
    return (this.results.value() ?? [])
      .filter((p) => !taken.has(p.key) && !['library', 'plugin', 'service'].includes(p.kind))
      .slice(0, 8);
  });
  protected readonly note = computed(() => searchHint(this.query()));
  protected readonly open = computed(() => this.expanded() && !!this.query());
  protected readonly listed = computed(() => this.open() && this.options().length > 0);
  protected readonly activeId = computed(() =>
    this.listed() ? `${this.id}-o-${this.active()}` : null,
  );

  private timer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.timer));
  }

  focus(): void {
    this.box().nativeElement.focus();
  }

  protected type(value: string): void {
    this.text.set(value);
    this.expanded.set(true);
    this.active.set(0);
    clearTimeout(this.timer);
    // A short pause before asking: one request per word, not per key.
    this.timer = setTimeout(() => this.query.set(value.trim()), 200);
    if (!value.trim()) this.query.set('');
  }

  protected key(event: KeyboardEvent): void {
    const count = this.options().length;
    switch (event.key) {
      case 'ArrowDown':
      case 'ArrowUp':
        event.preventDefault();
        if (!this.expanded()) {
          this.expanded.set(true);
          return;
        }
        if (count)
          this.active.update((i) => (i + (event.key === 'ArrowDown' ? 1 : count - 1)) % count);
        return;
      case 'Enter': {
        const product = this.open() ? this.options()[this.active()] : undefined;
        if (product) {
          event.preventDefault();
          void this.pick(product);
        }
        return;
      }
      case 'Escape':
        if (this.open()) {
          event.preventDefault();
          this.expanded.set(false);
        }
        return;
    }
  }

  protected reopen(): void {
    if (this.query()) this.expanded.set(true);
  }

  protected closeSoon(): void {
    this.expanded.set(false);
  }

  protected async pick(product: CatalogProductView): Promise<void> {
    if (await this.store.add({ key: product.key, name: product.name })) {
      clearTimeout(this.timer);
      this.text.set('');
      this.query.set('');
      this.active.set(0);
      this.focus();
    }
  }
}
