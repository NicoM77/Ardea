import {
  afterNextRender,
  Component,
  ElementRef,
  inject,
  Injector,
  signal,
  viewChild,
} from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { LucideTrash } from '@lucide/angular';
import { UiButton, UiInput } from 'ui';
import type { ComponentView } from '../../core/api/client';
import { KIND_LABELS } from '../projects/component-format';
import { StackAdd } from './stack-add';
import { PRECISION_HINTS, precisionOf, THRESHOLDS, type Precision } from './stack-format';
import { StackStore } from './stack-store';

@Component({
  selector: 'ardea-stack-entries',
  imports: [TranslocoPipe, LucideTrash, UiButton, UiInput, StackAdd],
  template: `
    @if (store.entries().length) {
      <div class="head" aria-hidden="true">
        <span>{{ 'stack.columns.product' | transloco }}</span>
        <span>{{ 'stack.columns.version' | transloco }}</span>
        <span>{{ 'stack.columns.threshold' | transloco }}</span>
        <span></span>
      </div>
      <ul class="rows" [attr.aria-label]="'stack.entriesLabel' | transloco">
        @for (entry of store.entries(); track entry.id) {
          <li class="row" [attr.data-key]="entry.productKey">
            <div class="name">
              <b>{{ entry.name }}</b>
              <small>{{ kindLabels[entry.kind] ?? kindLabels['other'] | transloco }}</small>
            </div>

            <div class="version">
              <div class="version-fields">
                <input
                  uiInput
                  mono
                  type="text"
                  autocomplete="off"
                  spellcheck="false"
                  maxlength="100"
                  [id]="'stack-v-' + entry.id"
                  [value]="entry.version ?? ''"
                  [placeholder]="'stack.precision.anyPlaceholder' | transloco"
                  [disabled]="!store.canEdit()"
                  [readOnly]="store.isPending(entry.id)"
                  [attr.aria-busy]="store.isPending(entry.id)"
                  [attr.aria-label]="'stack.version.label' | transloco: { name: entry.name }"
                  [attr.aria-describedby]="'stack-k-' + entry.id"
                  (input)="typed.set({ id: entry.id, value: $any($event.target).value })"
                  (change)="saveVersion(entry, $any($event.target))"
                  (keydown.enter)="saveVersion(entry, $any($event.target))"
                />
                @if (entry.version) {
                  <select
                    uiInput
                    [disabled]="!store.canEdit() || store.isPending(entry.id)"
                    [attr.aria-label]="'stack.precision.label' | transloco: { name: entry.name }"
                    (change)="savePrecision(entry, $any($event.target))"
                  >
                    <option value="line" [selected]="entry.versionPrecision === 'line'">
                      {{ 'stack.precision.line' | transloco }}
                    </option>
                    <option value="exact" [selected]="entry.versionPrecision === 'exact'">
                      {{ 'stack.precision.exact' | transloco }}
                    </option>
                  </select>
                }
              </div>
              <small class="kind" [id]="'stack-k-' + entry.id">{{
                precisionHint(entry) | transloco
              }}</small>
            </div>

            <div
              class="segmented"
              role="group"
              [attr.aria-label]="'stack.threshold.label' | transloco: { name: entry.name }"
            >
              @for (t of thresholds; track t.value) {
                <button
                  type="button"
                  [attr.aria-pressed]="entry.noticeThreshold === t.value"
                  [disabled]="!store.canEdit() || store.isPending(entry.id)"
                  (click)="store.setThreshold(entry, t.value)"
                >
                  {{ t.label | transloco }}
                </button>
              }
            </div>

            @if (store.canEdit()) {
              <button
                uiButton
                variant="ghost"
                iconOnly
                type="button"
                class="remove"
                [attr.aria-label]="'stack.remove' | transloco: { name: entry.name }"
                [disabled]="store.isPending(entry.id)"
                (click)="remove(entry)"
              >
                <svg lucideTrash aria-hidden="true"></svg>
              </button>
            }
          </li>
        }
      </ul>
    } @else {
      <p class="empty">{{ 'stack.empty' | transloco }}</p>
    }

    @if (store.canEdit()) {
      <ardea-stack-add />
    }
  `,
  styles: `
    :host {
      display: grid;
      gap: var(--space-lg);
      min-width: 0;
    }
    .head,
    .row {
      display: grid;
      grid-template-columns: minmax(0, 1fr) minmax(0, 200px) 228px 32px;
      column-gap: 14px;
      align-items: center;
    }
    .head {
      color: var(--muted);
      font-size: var(--fs-small);
      line-height: var(--lh-small);
      margin-bottom: calc(-1 * var(--space-sm));
    }
    .rows {
      display: grid;
      margin: 0;
      padding: 0;
      list-style: none;
    }
    .row {
      min-height: 64px;
      padding: var(--space-sm) 0;
      border-top: 1px solid var(--rule);
    }
    .name {
      display: grid;
      min-width: 0;
    }
    .name b {
      font-weight: 500;
      overflow-wrap: anywhere;
    }
    small {
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .version {
      display: grid;
      gap: 2px;
      min-width: 0;
    }
    .version-fields {
      display: flex;
      gap: 6px;
      min-width: 0;
    }
    .version-fields input {
      flex: 1 1 64px;
      min-width: 0;
    }
    .version-fields select {
      flex: 0 0 auto;
      width: auto;
    }
    .version .kind {
      color: var(--faint);
      font-size: var(--fs-small);
      line-height: var(--lh-small);
    }
    .segmented {
      display: inline-flex;
      padding: 2px;
      border-radius: var(--radius-control);
      background: var(--raised);
      border: 1px solid var(--border);
    }
    .segmented button {
      flex: 1;
      height: 28px;
      padding: 0 10px;
      border: 0;
      border-radius: var(--radius-key);
      background: none;
      color: var(--muted);
      font-size: var(--fs-sub);
      white-space: nowrap;
      cursor: pointer;
      transition:
        background-color var(--t-hover) ease,
        color var(--t-hover) ease;
    }
    .segmented button[aria-pressed='true'] {
      background: var(--surface);
      color: var(--text);
      font-weight: 500;
      box-shadow: 0 0 0 1px var(--border);
    }
    .segmented button:focus-visible {
      outline: var(--focus-ring);
      outline-offset: var(--focus-offset);
    }
    .segmented button:disabled {
      cursor: default;
    }
    .empty {
      color: var(--muted);
    }
    @media (hover: hover) and (pointer: fine) {
      .segmented button:not(:disabled):hover {
        color: var(--text);
      }
    }
    @media (max-width: 760px) {
      .head {
        display: none;
      }
      .row {
        grid-template-columns: minmax(0, 1fr) auto;
        grid-template-areas:
          'name remove'
          'version version'
          'threshold threshold';
        row-gap: var(--space-sm);
        padding: var(--space-md) 0;
      }
      .name {
        grid-area: name;
      }
      .version {
        grid-area: version;
      }
      .segmented {
        grid-area: threshold;
        display: flex;
      }
      .segmented button {
        height: 36px;
      }
      .remove {
        grid-area: remove;
      }
    }
  `,
})
export class StackEntries {
  protected readonly store = inject(StackStore);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly adder = viewChild(StackAdd);

  protected readonly kindLabels = KIND_LABELS;
  protected readonly thresholds = THRESHOLDS;
  protected readonly typed = signal<{ id: string; value: string } | null>(null);

  protected precisionHint(entry: ComponentView): string {
    const typing = this.typed();
    const precision =
      typing?.id === entry.id && typing.value.trim() !== (entry.version ?? '')
        ? precisionOf(typing.value)
        : (entry.versionPrecision as Precision);
    return PRECISION_HINTS[precision] ?? PRECISION_HINTS.none;
  }

  protected async saveVersion(entry: ComponentView, input: HTMLInputElement): Promise<void> {
    if (this.store.isPending(entry.id)) return;
    const version = input.value.trim();
    this.typed.set(null);
    if (!(await this.store.setVersion(entry, version, precisionOf(version)))) {
      input.value = this.stored(entry)?.version ?? '';
    }
  }

  protected async savePrecision(entry: ComponentView, select: HTMLSelectElement): Promise<void> {
    if (!entry.version) return;
    const saved = await this.store.setVersion(entry, entry.version, select.value as Precision);
    if (!saved) select.value = this.stored(entry)?.versionPrecision ?? entry.versionPrecision;
    afterNextRender(
      () => {
        if (select.isConnected && document.activeElement === document.body) select.focus();
      },
      { injector: this.injector },
    );
  }

  private stored(entry: ComponentView): ComponentView | undefined {
    return this.store.entries().find((e) => e.id === entry.id) ?? entry;
  }

  protected async remove(entry: ComponentView): Promise<void> {
    const at = this.store.entries().findIndex((e) => e.id === entry.id);
    if (!(await this.store.remove(entry))) return;
    afterNextRender(
      () => {
        const buttons = this.host.nativeElement.querySelectorAll<HTMLButtonElement>('.remove');
        const next = buttons[Math.min(at, buttons.length - 1)];
        if (next) next.focus();
        else this.adder()?.focus();
      },
      { injector: this.injector },
    );
  }

  focusSearch(): void {
    this.adder()?.focus();
  }
}
