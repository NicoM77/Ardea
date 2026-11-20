import { Component, computed, model, signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { UiButton, UiInput } from 'ui';
import {
  KIND_LABELS,
  PACKAGING_LABELS,
  withoutDraft,
  type DraftComponent,
} from './component-format';

@Component({
  selector: 'ardea-draft-list',
  imports: [TranslocoPipe, UiButton, UiInput],
  template: `
    @for (draft of drafts(); track draft.ref) {
      <div class="item" role="group" [attr.aria-label]="draft.name">
        <div class="head">
          <span class="what">
            <b>{{ draft.name }}</b>
            <small>
              {{ kindLabels[draft.kind] ?? draft.kind | transloco }}
              @if (parentName(draft); as parent) {
                · {{ 'projects.drafts.pluginOf' | transloco: { name: parent } }}
              }
              @if (!draft.productKey) {
                · {{ 'projects.drafts.free' | transloco }}
              }
            </small>
          </span>
          <span class="actions">
            <button
              uiButton
              variant="ghost"
              type="button"
              [attr.aria-expanded]="open() === draft.ref"
              (click)="open.set(open() === draft.ref ? null : draft.ref)"
            >
              {{ 'projects.drafts.more' | transloco }}
            </button>
            <button
              uiButton
              variant="ghost"
              type="button"
              [attr.aria-label]="'projects.drafts.removeNamed' | transloco: { name: draft.name }"
              (click)="remove(draft)"
            >
              {{ 'projects.drafts.remove' | transloco }}
            </button>
          </span>
        </div>
        @if (draft.kind !== 'service') {
          <div class="fields">
            <label class="field">
              <span>{{ 'projects.drafts.version' | transloco }}</span>
              <input
                uiInput
                type="text"
                autocomplete="off"
                spellcheck="false"
                [value]="draft.version"
                [disabled]="draft.versionUnknown"
                (input)="set(draft, { version: $any($event.target).value })"
              />
            </label>
            <label class="check">
              <input
                type="checkbox"
                [checked]="draft.versionUnknown"
                (change)="set(draft, { versionUnknown: $any($event.target).checked })"
              />
              {{ 'projects.drafts.versionUnknown' | transloco }}
            </label>
          </div>
        } @else {
          <p class="note">{{ 'projects.drafts.serviceNoVersion' | transloco }}</p>
        }
        @if (open() === draft.ref) {
          <div class="fields">
            <label class="field">
              <span>{{ 'projects.drafts.packaging' | transloco }}</span>
              <select uiInput (change)="set(draft, { packaging: $any($event.target).value })">
                @for (p of packagings; track p) {
                  <option [value]="p" [selected]="p === draft.packaging">
                    {{ packagingLabels[p] | transloco }}
                  </option>
                }
              </select>
            </label>
            <label class="field">
              <span>{{ 'projects.drafts.protection' | transloco }}</span>
              <select uiInput (change)="set(draft, { protection: $any($event.target).value })">
                <option value="none" [selected]="draft.protection === 'none'">
                  {{ 'projects.protection.none' | transloco }}
                </option>
                <option value="effective" [selected]="draft.protection === 'effective'">
                  {{ 'projects.protection.effective' | transloco }}
                </option>
              </select>
            </label>
            @if (draft.protection === 'effective') {
              <label class="field wide">
                <span>{{ 'projects.drafts.protectionNote' | transloco }}</span>
                <input
                  uiInput
                  type="text"
                  [value]="draft.protectionNote"
                  (input)="set(draft, { protectionNote: $any($event.target).value })"
                />
              </label>
            }
          </div>
        }
      </div>
    } @empty {
      <p class="note">{{ 'projects.drafts.empty' | transloco }}</p>
    }
  `,
  styles: `
    :host {
      display: grid;
      min-width: 0;
    }
    .item {
      display: grid;
      gap: var(--space-sm);
      padding: var(--space-md) 0;
      border-top: 1px solid var(--rule);
    }
    .item:first-child {
      border-top: 0;
      padding-top: 0;
    }
    .head {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-start;
      justify-content: space-between;
      gap: var(--space-sm);
    }
    .what {
      display: grid;
      min-width: 0;
    }
    .what b {
      font-weight: 500;
    }
    small,
    .note {
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .actions {
      display: flex;
      gap: var(--space-xs);
    }
    .fields {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-end;
      gap: var(--space-sm) var(--space-md);
    }
    .field {
      display: grid;
      gap: 4px;
      flex: 0 1 200px;
    }
    .field.wide {
      flex: 1 1 280px;
    }
    .field > span {
      font-size: var(--fs-sub);
      font-weight: 500;
    }
    .check {
      display: inline-flex;
      align-items: center;
      gap: var(--space-sm);
      height: var(--control-height);
    }
    .check input {
      width: 16px;
      height: 16px;
      margin: 0;
      accent-color: var(--text);
    }
    .check input:focus-visible {
      outline: var(--focus-ring);
      outline-offset: var(--focus-offset);
    }
  `,
})
export class DraftList {
  readonly drafts = model.required<DraftComponent[]>();

  protected readonly kindLabels = KIND_LABELS;
  protected readonly packagings = Object.keys(PACKAGING_LABELS);
  protected readonly packagingLabels = PACKAGING_LABELS;
  protected readonly open = signal<string | null>(null);
  private readonly byRef = computed(() => new Map(this.drafts().map((d) => [d.ref, d])));
  private readonly byId = computed(
    () =>
      new Map(
        this.drafts()
          .filter((d) => d.id)
          .map((d) => [d.id!, d]),
      ),
  );

  protected parentName(draft: DraftComponent): string | null {
    const parent =
      (draft.parentId && this.byId().get(draft.parentId)) ||
      (draft.parentRef && this.byRef().get(draft.parentRef));
    return parent ? parent.name : null;
  }

  protected set(draft: DraftComponent, change: Partial<DraftComponent>): void {
    this.drafts.update((list) => list.map((d) => (d.ref === draft.ref ? { ...d, ...change } : d)));
  }

  protected remove(draft: DraftComponent): void {
    this.drafts.update((list) => withoutDraft(list, draft.ref));
  }
}
