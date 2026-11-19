import { Component, computed, input, model, output } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { UiButton, UiCard, UiNotice } from 'ui';
import type { ComponentView } from '../../core/api/client';
import { CatalogPicker, type Picked } from '../projects/catalog-picker';
import {
  addChoice,
  ecosystems,
  isPackage,
  type DraftComponent,
} from '../projects/component-format';
import { DraftList } from '../projects/draft-list';
import { PackagesView } from '../projects/packages-view';

/**
 * Step "Technologies" (master plan 11.6, 16.7) in three groups: what came from the repositories (a
 * summary per ecosystem and what cannot be checked), the suggestions to confirm (projected by the
 * wizard when repositories are linked), and what the repositories do not show, entered by hand
 * with the catalogue search. Without a repository only the last group is there.
 */
@Component({
  selector: 'ardea-technologies-step',
  imports: [TranslocoPipe, UiButton, UiCard, UiNotice, CatalogPicker, DraftList, PackagesView],
  template: `
    <div class="stack">
      @if (fromRepository().length) {
        <ui-card [heading]="'wizard.technologies.fromRepository' | transloco">
          <span uiCardMeta>{{
            'wizard.technologies.packageCount' | transloco: { count: packageCount() }
          }}</span>
          <div class="group">
            <p class="hint">{{ 'wizard.technologies.fromRepositoryHint' | transloco }}</p>
            <ardea-packages-view [components]="fromRepository()" />
            @if (gaps().length) {
              <h3>{{ 'wizard.technologies.gapsTitle' | transloco }}</h3>
              <ul class="gaps">
                @for (gap of gaps(); track gap.key) {
                  <li>
                    <span class="n">{{ gap.count }}</span>
                    <span>{{ gap.key | transloco: gap.params }}</span>
                  </li>
                }
              </ul>
            }
          </div>
        </ui-card>
      }

      <ng-content />

      <ui-card
        [heading]="
          (fromRepository().length ? 'wizard.technologies.notShown' : 'wizard.technologies.byHand')
            | transloco
        "
      >
        <div class="group">
          <p class="hint">{{ 'wizard.technologies.byHandHint' | transloco }}</p>
          <ardea-catalog-picker (picked)="add($event)" />
          <h3>{{ 'wizard.technologies.added' | transloco }}</h3>
          <ardea-draft-list [(drafts)]="drafts" />
        </div>
      </ui-card>

      @if (problem()) {
        <ui-notice tone="error">{{ problem() | transloco }}</ui-notice>
      }
      <div class="actions">
        <button uiButton variant="ghost" type="button" (click)="back.emit()">
          {{ 'wizard.back' | transloco }}
        </button>
        <button
          uiButton
          variant="primary"
          type="button"
          [loading]="saving()"
          (click)="continued.emit()"
        >
          {{ finishLabel() | transloco }}
        </button>
      </div>
    </div>
  `,
  styles: `
    :host {
      display: block;
    }
    .stack {
      display: grid;
      gap: var(--gap);
    }
    .group {
      display: grid;
      gap: var(--space-md);
    }
    h3 {
      margin-top: var(--space-sm);
      font-weight: 600;
    }
    .hint {
      color: var(--muted);
      max-width: 72ch;
    }
    .gaps li {
      display: grid;
      grid-template-columns: 44px minmax(0, 1fr);
      gap: var(--space-md);
      padding: var(--space-sm) 0;
      border-top: 1px solid var(--rule);
    }
    .gaps li:first-child {
      border-top: 0;
    }
    .n {
      font-weight: 600;
      text-align: right;
    }
    .actions {
      display: flex;
      justify-content: space-between;
      gap: var(--space-md);
      padding-top: var(--space-lg);
      border-top: 1px solid var(--border);
    }
  `,
})
export class TechnologiesStep {
  readonly components = input.required<readonly ComponentView[]>();
  readonly drafts = model.required<DraftComponent[]>();
  readonly saving = input(false);
  readonly problem = input<string | null>(null);
  readonly finishLabel = input('wizard.finish');
  readonly continued = output<void>();
  readonly back = output<void>();

  protected readonly fromRepository = computed(() =>
    this.components().filter((c) => c.origin === 'repository'),
  );

  protected readonly packageCount = computed(() => this.fromRepository().filter(isPackage).length);

  /** What cannot be checked, and why (master plan 16.7; the full hints are task 2.8). */
  protected readonly gaps = computed(() => {
    const list: { key: string; count: number; params: Record<string, string | number> }[] = [];
    for (const eco of ecosystems(this.fromRepository())) {
      if (!eco.checkable) {
        list.push({
          key: 'wizard.technologies.gapEcosystem',
          count: eco.packages.length,
          params: { name: eco.name },
        });
      } else if (eco.versionUnknown) {
        list.push({
          key: 'wizard.technologies.gapVersion',
          count: eco.versionUnknown,
          params: { name: eco.name },
        });
      }
    }
    return list;
  });

  protected add(choice: Picked): void {
    this.drafts.set(addChoice(this.drafts(), choice).list);
  }
}
