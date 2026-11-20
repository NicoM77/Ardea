import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { LucideDynamicIcon, LucideSearch } from '@lucide/angular';
import { UiKbd } from 'ui';
import {
  filterSearchItems,
  moveActive,
  SearchItem,
  VulnerabilitySearch,
  withVulnerabilities,
} from './search';

export interface SearchDialogData {
  items: readonly SearchItem[];
  vulnerabilities: VulnerabilitySearch;
  byPointer: boolean;
}

@Component({
  selector: 'ardea-search-dialog',
  imports: [TranslocoPipe, LucideDynamicIcon, LucideSearch, UiKbd],
  host: { '[class.by-pointer]': 'data.byPointer' },
  template: `
    <div class="input-row">
      <svg lucideSearch aria-hidden="true"></svg>
      <input
        #box
        type="text"
        role="combobox"
        autocomplete="off"
        spellcheck="false"
        aria-autocomplete="list"
        aria-expanded="true"
        aria-controls="search-results"
        [attr.aria-activedescendant]="active() >= 0 ? 'search-option-' + active() : null"
        [attr.aria-label]="'search.label' | transloco"
        [placeholder]="'search.placeholder' | transloco"
        [value]="query()"
        (input)="onInput(box.value)"
        (keydown)="onKeydown($event)"
      />
      <ui-kbd>{{ 'search.escKey' | transloco }}</ui-kbd>
    </div>
    <div
      class="list"
      id="search-results"
      role="listbox"
      [attr.aria-label]="'search.results' | transloco"
    >
      @for (group of groups(); track group.name; let g = $index) {
        <div role="group" [attr.aria-labelledby]="'search-group-' + g">
          <p class="group" [id]="'search-group-' + g">{{ group.name }}</p>
          @for (entry of group.entries; track entry.item.url) {
            <!-- Options of a combobox: focus stays in the input, the keyboard moves aria-activedescendant -->
            <!-- eslint-disable-next-line @angular-eslint/template/click-events-have-key-events, @angular-eslint/template/interactive-supports-focus -->
            <div
              class="option"
              role="option"
              [id]="'search-option-' + entry.index"
              [attr.aria-selected]="entry.index === active()"
              (click)="go(entry.item)"
              (pointermove)="active.set(entry.index)"
            >
              <svg [lucideIcon]="entry.item.icon" aria-hidden="true"></svg>
              <span>{{ entry.item.label }}</span>
            </div>
          }
        </div>
      }
    </div>
    <p class="foot" aria-hidden="true">
      <span><ui-kbd>↑</ui-kbd><ui-kbd>↓</ui-kbd> {{ 'search.hintMove' | transloco }}</span>
      <span><ui-kbd>↵</ui-kbd> {{ 'search.hintOpen' | transloco }}</span>
    </p>
  `,
  styles: `
    :host {
      display: block;
      width: min(560px, calc(100vw - 32px));
      border-radius: var(--radius-dialog);
      background: var(--surface);
      box-shadow: var(--shadow-dialog);
      overflow: hidden;
    }
    :host(.by-pointer) {
      animation: search-pop 180ms var(--ease-out) both;
    }
    @keyframes search-pop {
      from {
        opacity: 0;
        transform: scale(0.97);
      }
    }
    .input-row {
      display: flex;
      align-items: center;
      gap: 10px;
      height: 52px;
      padding: 0 var(--space-lg);
      border-bottom: 1px solid var(--border);
      color: var(--faint);
    }
    input {
      flex: 1;
      min-width: 0;
      height: 100%;
      border: 0;
      outline: 0;
      background: none;
      color: var(--text);
      font-size: var(--fs-section);
    }
    input::placeholder {
      color: var(--faint);
    }
    .list {
      max-height: min(344px, 50vh);
      overflow-y: auto;
      padding: var(--space-sm);
    }
    .group {
      margin: var(--space-sm) var(--space-sm) var(--space-xs);
      color: var(--faint);
      font-size: var(--fs-small);
      line-height: var(--lh-small);
    }
    .option {
      display: flex;
      align-items: center;
      gap: 10px;
      height: 40px;
      padding: 0 var(--space-sm);
      border-radius: var(--radius-control);
      color: var(--text);
      cursor: pointer;
    }
    .option svg {
      color: var(--muted);
    }
    .option[aria-selected='true'] {
      background: var(--active);
    }
    .foot {
      display: flex;
      gap: var(--space-lg);
      padding: var(--space-sm) var(--space-lg);
      border-top: 1px solid var(--rule);
      color: var(--faint);
      font-size: var(--fs-small);
      line-height: var(--lh-small);
    }
    .foot span {
      display: inline-flex;
      align-items: center;
      gap: var(--space-xs);
    }
    @media (prefers-reduced-motion: reduce) {
      :host(.by-pointer) {
        animation: search-fade 150ms ease both;
      }
      @keyframes search-fade {
        from {
          opacity: 0;
        }
      }
    }
    @media (max-width: 640px) {
      .foot {
        display: none;
      }
    }
  `,
})
export class SearchDialog {
  protected readonly data = inject<SearchDialogData>(DIALOG_DATA);
  private readonly ref = inject(DialogRef);
  private readonly router = inject(Router);

  protected readonly query = signal('');
  protected readonly results = computed(() =>
    withVulnerabilities(
      filterSearchItems(this.data.items, this.query()),
      this.query(),
      this.data.vulnerabilities,
    ),
  );
  protected readonly active = signal(0);
  protected readonly groups = computed(() => {
    const groups: { name: string; entries: { item: SearchItem; index: number }[] }[] = [];
    this.results().forEach((item, index) => {
      const last = groups[groups.length - 1];
      if (last?.name === item.group) last.entries.push({ item, index });
      else groups.push({ name: item.group, entries: [{ item, index }] });
    });
    return groups;
  });

  protected onInput(value: string): void {
    this.query.set(value);
    this.active.set(this.results().length ? 0 : -1);
  }

  protected onKeydown(event: KeyboardEvent): void {
    const count = this.results().length;
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      this.active.set(moveActive(this.active(), event.key === 'ArrowDown' ? 1 : -1, count));
      document
        .getElementById(`search-option-${this.active()}`)
        ?.scrollIntoView({ block: 'nearest' });
    } else if (event.key === 'Enter') {
      event.preventDefault();
      const item = this.results()[this.active()];
      if (item) this.go(item);
    }
  }

  protected go(item: SearchItem): void {
    this.ref.close();
    void this.router.navigateByUrl(item.url);
  }
}
