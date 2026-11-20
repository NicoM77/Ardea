import { afterNextRender, Component, computed, ElementRef, inject, Injector } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { LucidePlus } from '@lucide/angular';
import { UiButton } from 'ui';
import type { StackSuggestionView } from '../../core/api/client';
import { groupCommon, suggestionReason } from './stack-format';
import { StackStore } from './stack-store';

@Component({
  selector: 'ardea-stack-suggestions',
  imports: [TranslocoPipe, LucidePlus, UiButton],
  template: `
    @if (reasoned().length) {
      <ul class="reasoned">
        @for (s of reasoned(); track s.productKey) {
          <li>
            <span class="what">
              <b>{{ s.name }}</b>
              <small>{{ reason(s).key | transloco: reason(s).params }}</small>
            </span>
            <button
              uiButton
              type="button"
              [attr.data-suggest]="s.productKey"
              [attr.aria-label]="'stack.suggest.addNamed' | transloco: { name: s.name }"
              [disabled]="!store.canEdit() || store.isPending(s.productKey)"
              (click)="add(s)"
            >
              {{ 'stack.suggest.add' | transloco }}
            </button>
          </li>
        }
      </ul>
    }

    @if (groups().length) {
      <h3>{{ 'stack.suggest.common' | transloco }}</h3>
      <div class="groups">
        @for (group of groups(); track group.id) {
          <div class="group" role="group" [attr.aria-labelledby]="'stack-g-' + group.id">
            <span class="group-label" [id]="'stack-g-' + group.id">{{
              group.label | transloco
            }}</span>
            <div class="chips">
              @for (s of group.items; track s.productKey) {
                <button
                  type="button"
                  class="chip"
                  [attr.data-suggest]="s.productKey"
                  [attr.aria-label]="'stack.suggest.addNamed' | transloco: { name: s.name }"
                  [disabled]="!store.canEdit() || store.isPending(s.productKey)"
                  (click)="add(s)"
                >
                  <svg lucidePlus aria-hidden="true"></svg>{{ s.name }}
                </button>
              }
            </div>
          </div>
        }
      </div>
    } @else if (!reasoned().length && loaded()) {
      <p class="muted">{{ 'stack.suggest.none' | transloco }}</p>
    }
  `,
  styles: `
    :host {
      display: grid;
      gap: var(--space-md);
      min-width: 0;
    }
    .reasoned {
      display: grid;
      margin: 0;
      padding: 0;
      list-style: none;
    }
    .reasoned li {
      display: flex;
      align-items: center;
      gap: var(--space-md);
      min-height: 52px;
      padding: var(--space-sm) 0;
      border-top: 1px solid var(--rule);
    }
    .reasoned li:first-child {
      border-top: 0;
      padding-top: 0;
    }
    .what {
      display: grid;
      flex: 1;
      min-width: 0;
    }
    .what b {
      font-weight: 500;
    }
    small,
    .muted,
    .group-label {
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    h3 {
      font-size: var(--fs-sub);
      font-weight: 500;
      line-height: var(--lh-sub);
    }
    .groups {
      display: grid;
      gap: var(--space-md);
    }
    .group {
      display: grid;
      gap: 6px;
    }
    .chips {
      display: flex;
      flex-wrap: wrap;
      gap: 6px;
    }
    .chip {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      height: 28px;
      padding: 0 10px 0 8px;
      border: 1px solid var(--border);
      border-radius: var(--radius-pill);
      background: var(--surface);
      color: var(--text);
      font-size: var(--fs-sub);
      cursor: pointer;
      transition:
        background-color var(--t-hover) ease,
        border-color var(--t-hover) ease;
    }
    .chip svg {
      width: 12px;
      height: 12px;
      color: var(--muted);
    }
    .chip:focus-visible {
      outline: var(--focus-ring);
      outline-offset: var(--focus-offset);
    }
    .chip:disabled {
      cursor: default;
      color: var(--faint);
    }
    @media (hover: hover) and (pointer: fine) {
      .chip:not(:disabled):hover {
        border-color: var(--control);
        background: var(--raised);
      }
    }
    @media (max-width: 640px) {
      .chip {
        height: 36px;
      }
    }
  `,
})
export class StackSuggestions {
  protected readonly store = inject(StackStore);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  protected readonly loaded = computed(() => this.store.suggestions.hasValue());
  protected readonly reasoned = computed(() =>
    this.store.suggested().filter((s) => s.reason !== 'common'),
  );
  protected readonly groups = computed(() => groupCommon(this.store.suggested()));
  protected readonly reason = suggestionReason;

  protected async add(s: StackSuggestionView): Promise<void> {
    const buttons = [...this.host.nativeElement.querySelectorAll<HTMLElement>('[data-suggest]')];
    const at = buttons.findIndex((b) => b.dataset['suggest'] === s.productKey);
    if (!(await this.store.add({ key: s.productKey, name: s.name }))) return;
    afterNextRender(
      () => {
        const now = this.host.nativeElement.querySelectorAll<HTMLElement>('[data-suggest]');
        now[Math.min(at, now.length - 1)]?.focus();
      },
      { injector: this.injector },
    );
  }
}
