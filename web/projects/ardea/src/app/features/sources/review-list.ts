import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { LucideCircleCheck } from '@lucide/angular';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiCard, UiLoader, UiNotice, UiToaster } from 'ui';
import {
  listReviewItemsResource,
  SourcesService,
  type ReviewItemView,
} from '../../core/api/client';
import { problemMessageKey } from '../../core/api/problem';
import { Dates } from '../../core/format/dates';

@Component({
  selector: 'ardea-review-list',
  imports: [
    NgTemplateOutlet,
    RouterLink,
    TranslocoPipe,
    LucideCircleCheck,
    UiButton,
    UiCard,
    UiLoader,
    UiNotice,
  ],
  template: `
    <ui-card [heading]="'review.title' | transloco" [attr.aria-busy]="open.isLoading()">
      <label uiCardMeta class="toggle">
        <input
          type="checkbox"
          [checked]="showDecided()"
          (change)="showDecided.set(!showDecided())"
        />
        {{ 'review.showDecided' | transloco }}
      </label>
      <p class="intro">{{ 'review.intro' | transloco }}</p>

      @if (problem()) {
        <ui-notice class="gap" tone="error">{{ problem() | transloco }}</ui-notice>
      }

      @if (open.hasValue() && open.value(); as cases) {
        @if (cases.length) {
          <ul class="cases">
            @for (item of cases; track item.id) {
              <li>
                <ng-container
                  [ngTemplateOutlet]="caseBody"
                  [ngTemplateOutletContext]="{ $implicit: item }"
                />
                <div class="act">
                  <button
                    uiButton
                    type="button"
                    [loading]="busy() === item.id"
                    (click)="keepSeparate(item)"
                  >
                    {{ 'review.keepSeparate' | transloco }}
                  </button>
                </div>
              </li>
            }
          </ul>
        } @else {
          <p class="healthy">
            <svg lucideCircleCheck aria-hidden="true"></svg>
            {{ 'review.empty' | transloco }}
          </p>
        }
      } @else if (open.error()) {
        <ui-notice tone="error">{{ 'review.loadFailed' | transloco }}</ui-notice>
        <div class="gap">
          <button uiButton type="button" (click)="open.reload()">
            {{ 'entries.tryAgain' | transloco }}
          </button>
        </div>
      } @else {
        <ui-loader [label]="'review.loading' | transloco" />
      }

      @if (showDecided()) {
        <h3 class="decided-title">{{ 'review.decidedTitle' | transloco }}</h3>
        @if (decidedList.hasValue() && decidedList.value(); as cases) {
          @if (cases.length) {
            <ul class="cases">
              @for (item of cases; track item.id) {
                <li>
                  <ng-container
                    [ngTemplateOutlet]="caseBody"
                    [ngTemplateOutletContext]="{ $implicit: item }"
                  />
                  @if (item.decidedAt) {
                    <span class="sub">{{
                      'review.decidedAt' | transloco: { when: when(item.decidedAt) }
                    }}</span>
                  }
                </li>
              }
            </ul>
          } @else {
            <p class="muted">{{ 'review.noDecided' | transloco }}</p>
          }
        } @else if (decidedList.error()) {
          <p class="muted">{{ 'review.loadFailed' | transloco }}</p>
        } @else {
          <ui-loader [label]="'review.loading' | transloco" />
        }
      }

      <ng-template #caseBody let-item>
        <ul class="entries">
          @for (entry of item.entries; track entry.primaryKey) {
            <li>
              <a class="link mono" [routerLink]="['/entries', entry.primaryKey]">{{
                entry.primaryKey
              }}</a>
              @if (entry.title) {
                <span class="title">{{ entry.title }}</span>
              }
            </li>
          }
        </ul>
        <span class="sub">
          {{ 'review.joinedBy' | transloco }}
          <span class="mono">{{ item.sharedAliases.join(', ') }}</span>
          ·
          {{
            'review.seen'
              | transloco: { first: when(item.firstSeenAt), last: when(item.lastSeenAt) }
          }}
        </span>
      </ng-template>
    </ui-card>
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }
    .toggle {
      display: inline-flex;
      align-items: center;
      gap: var(--space-sm);
      color: var(--muted);
      font-size: var(--fs-sub);
      cursor: pointer;
    }
    .toggle input {
      width: 16px;
      height: 16px;
      margin: 0;
      accent-color: var(--text);
    }
    .toggle input:focus-visible {
      outline: var(--focus-ring);
      outline-offset: var(--focus-offset);
    }
    .healthy {
      display: flex;
      align-items: center;
      gap: var(--space-sm);
      color: var(--muted);
    }
    .healthy svg {
      flex: none;
      width: 16px;
      height: 16px;
      color: var(--positive);
    }
    .intro {
      margin: calc(-1 * var(--space-sm)) 0 var(--space-md);
      color: var(--muted);
      max-width: 72ch;
    }
    .gap {
      display: block;
      margin: var(--space-md) 0;
    }
    .cases {
      display: grid;
    }
    .cases > li {
      display: grid;
      grid-template-columns: minmax(0, 1fr) auto;
      gap: var(--space-xs) var(--gap);
      padding: var(--space-md) 0;
      border-top: 1px solid var(--rule);
    }
    .cases > li > :not(.act) {
      grid-column: 1;
    }
    .act {
      grid-column: 2;
      grid-row: 1 / span 2;
      align-self: start;
    }
    .entries {
      display: grid;
      gap: 2px;
    }
    .entries li {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-xs) var(--space-md);
      min-width: 0;
    }
    .entries .link {
      display: inline-flex;
      align-items: center;
      min-height: 24px;
    }
    .title {
      overflow-wrap: anywhere;
    }
    .mono {
      font-family: var(--mono);
      font-size: var(--fs-sub);
    }
    .sub {
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .muted {
      color: var(--muted);
    }
    .decided-title {
      margin-top: var(--gap);
      margin-bottom: var(--space-xs);
    }
    @media (max-width: 640px) {
      .cases > li {
        grid-template-columns: minmax(0, 1fr);
      }
      .act {
        grid-column: 1;
        grid-row: auto;
      }
    }
  `,
})
export class ReviewList {
  private readonly api = inject(SourcesService);
  private readonly transloco = inject(TranslocoService);
  private readonly toaster = inject(UiToaster);

  protected readonly showDecided = signal(false);
  protected readonly open = listReviewItemsResource(signal({ state: 'open' }));
  protected readonly decidedList = listReviewItemsResource(
    computed(() => (this.showDecided() ? { state: 'kept_separate' } : undefined)),
  );

  protected readonly busy = signal<string | null>(null);
  protected readonly problem = signal<string | null>(null);

  protected async keepSeparate(item: ReviewItemView): Promise<void> {
    this.busy.set(item.id);
    this.problem.set(null);
    try {
      await firstValueFrom(this.api.keepReviewItemSeparate(item.id));
      this.toaster.show({
        text: this.transloco.translate('review.keptSeparate', {
          keys: item.entries.map((entry) => entry.primaryKey).join(', '),
        }),
      });
    } catch (error) {
      this.problem.set(
        problemMessageKey(error, {
          statuses: { 404: 'review.gone' },
          fallback: 'review.failed',
        }),
      );
    } finally {
      this.busy.set(null);
      this.open.reload();
      if (this.showDecided()) this.decidedList.reload();
    }
  }

  private readonly dates = inject(Dates);

  protected when(value: string): string {
    return this.dates.dateTime(value);
  }
}
