import { Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { LucideChevronRight, LucideMegaphone } from '@lucide/angular';
import { UiCard, type UiPriorityLevel } from 'ui';
import type { ChangeCountView, EntrySummary, SourceStatus } from '../../core/api/client';
import { Dates } from '../../core/format/dates';
import { num } from '../actions/action-format';
import { SourceHealth } from '../sources/source-health';
import { sourceSummary, warningSource } from './overview-format';

const ROWS = `
  .rows {
    display: flex;
    flex-direction: column;
  }
  .rows > li > a,
  .rows > li > .row {
    display: flex;
    align-items: center;
    gap: var(--space-md);
    min-height: 52px;
    margin: 0 calc(-1 * var(--space-sm));
    padding: var(--space-sm);
    border-radius: var(--radius-control);
    transition: background-color var(--t-hover) ease;
  }
  .rows > li + li {
    border-top: 1px solid var(--rule);
  }
  .main {
    flex: 1;
    min-width: 0;
  }
  .main b,
  .main small {
    display: block;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .main b {
    font-weight: 500;
  }
  .main small,
  .end,
  .none,
  .note {
    color: var(--muted);
    font-size: var(--fs-sub);
    line-height: var(--lh-sub);
  }
  .end {
    flex: none;
    white-space: nowrap;
  }
  .icon {
    display: grid;
    place-items: center;
    width: 24px;
    height: 24px;
    flex: none;
    color: var(--muted);
  }
  .note {
    margin-bottom: var(--space-sm);
  }
  .none {
    padding: var(--space-sm) 0;
  }
  .card-link {
    display: inline-flex;
    align-items: center;
    gap: 2px;
    color: var(--muted);
    font-size: var(--fs-sub);
  }
  @media (hover: hover) and (pointer: fine) {
    .rows a:hover {
      background: var(--raised);
    }
    .card-link:hover {
      color: var(--text);
    }
  }
`;

@Component({
  selector: 'ardea-projects-card',
  imports: [RouterLink, TranslocoPipe, UiCard],
  template: `
    <ui-card [heading]="'overview.projects.title' | transloco">
      <ul class="rows">
        @for (p of projects(); track p.id) {
          <li>
            <a routerLink="../reminders" [queryParams]="{ project: p.id }">
              <span class="main"
                ><b>{{ p.name }}</b></span
              >
              <span class="blocks" aria-hidden="true" [style.--n]="most()">
                @for (b of p.priorities; track $index) {
                  <i [attr.data-p]="b"></i>
                }
              </span>
              <span class="count"
                >{{ p.count
                }}<span class="sr-only">{{ 'overview.projects.open' | transloco }}</span></span
              >
            </a>
          </li>
        } @empty {
          <li class="none">{{ 'overview.projects.none' | transloco }}</li>
        }
      </ul>
    </ui-card>
  `,
  styles:
    ROWS +
    `
    .blocks {
      display: flex;
      gap: 2px;
      width: 132px;
      flex: none;
      transform-origin: left center;
      animation: grow var(--t-draw) var(--ease-out) both;
    }
    @keyframes grow {
      from {
        transform: scaleX(0);
      }
    }
    @media (prefers-reduced-motion: reduce) {
      .blocks {
        animation: none;
      }
    }
    .blocks i {
      height: 12px;
      flex: 0 0 calc((100% - (var(--n, 3) - 1) * 2px) / var(--n, 3));
      border-radius: 2px;
      background: var(--low);
    }
    .blocks i[data-p='critical'] {
      background: var(--critical);
    }
    .blocks i[data-p='high'] {
      background: var(--high);
    }
    .blocks i[data-p='medium'] {
      background: var(--medium);
    }
    .count {
      min-width: 20px;
      text-align: right;
      font-weight: 500;
    }
  `,
})
export class ProjectsCard {
  readonly projects =
    input.required<{ id: string; name: string; count: number; priorities: UiPriorityLevel[] }[]>();
  protected readonly most = computed(() =>
    Math.max(3, ...this.projects().map((p) => p.priorities.length)),
  );
}

const CHANGE_LABELS: Readonly<Record<string, string>> = {
  'action.raised': 'overview.changes.raised',
  'action.level_raised': 'overview.changes.levelRaised',
  'action.level_lowered': 'overview.changes.levelLowered',
  'action.exploitation_escalated': 'overview.changes.exploited',
  'action.fix_available': 'overview.changes.fixAvailable',
  'action.withdrawn': 'overview.changes.withdrawn',
  'action.status_changed': 'overview.changes.status',
  'action.note_added': 'overview.changes.notes',
  'backfill.completed': 'overview.changes.backfill',
};

@Component({
  selector: 'ardea-changes-card',
  imports: [TranslocoPipe, UiCard],
  template: `
    <ui-card [heading]="'overview.changes.title' | transloco">
      <span uiCardMeta>{{ 'overview.changes.meta' | transloco: { count: total() } }}</span>
      @if (known().length) {
        <ul class="counts">
          @for (c of known(); track c.kind) {
            <li>{{ c.label | transloco: { count: c.count } }}</li>
          }
        </ul>
      } @else {
        <p class="none">{{ 'overview.changes.none' | transloco }}</p>
      }
    </ui-card>
  `,
  styles:
    ROWS +
    `
    .counts {
      display: grid;
      gap: var(--space-sm);
    }
  `,
})
export class ChangesCard {
  readonly changes = input.required<readonly ChangeCountView[]>();
  protected readonly known = computed(() =>
    this.changes()
      .filter((c) => CHANGE_LABELS[c.kind] && num(c.count) > 0)
      .map((c) => ({ kind: c.kind, label: CHANGE_LABELS[c.kind], count: num(c.count) })),
  );
  protected readonly total = computed(() => this.known().reduce((n, c) => n + c.count, 0));
}

@Component({
  selector: 'ardea-warnings-card',
  imports: [RouterLink, TranslocoPipe, LucideMegaphone, UiCard],
  template: `
    <ui-card [heading]="'overview.warnings.title' | transloco">
      <span uiCardMeta>{{ 'overview.warnings.meta' | transloco }}</span>
      <p class="note">{{ 'overview.warnings.note' | transloco }}</p>
      <ul class="rows">
        @for (w of warnings(); track w.key) {
          <li>
            <a [routerLink]="['/entries', w.key]">
              <span class="icon"><svg lucideMegaphone aria-hidden="true"></svg></span>
              <span class="main">
                <b>{{ w.title ?? w.key }}</b>
                <small>{{ source(w.key) ?? ('overview.warnings.national' | transloco) }}</small>
              </span>
              <time
                class="end"
                [attr.datetime]="w.publishedAt ?? w.changedAt"
                [title]="dates.dateTime(w.publishedAt ?? w.changedAt)"
                >{{ dates.relative(w.publishedAt ?? w.changedAt) }}</time
              >
            </a>
          </li>
        }
      </ul>
    </ui-card>
  `,
  styles: ROWS,
})
export class WarningsCard {
  readonly warnings = input.required<readonly EntrySummary[]>();
  readonly sources = input<readonly SourceStatus[]>([]);
  protected readonly dates = inject(Dates);

  protected source(key: string): string | null {
    return warningSource(key, this.sources());
  }
}

@Component({
  selector: 'ardea-sources-card',
  imports: [RouterLink, TranslocoPipe, LucideChevronRight, UiCard, SourceHealth],
  template: `
    <ui-card [heading]="'overview.sources.title' | transloco">
      <a uiCardMeta class="card-link" routerLink="/sources"
        >{{ 'overview.sources.all' | transloco }}<svg lucideChevronRight aria-hidden="true"></svg
      ></a>
      @if (summary(); as s) {
        <p class="summary">{{ s.key | transloco: s.params }}</p>
        @if (s.attention.length) {
          <ul class="rows">
            @for (source of s.attention; track source.key) {
              <li>
                <a routerLink="/sources">
                  <span class="main">
                    <b>{{ source.name }}</b>
                    <small
                      ><ardea-source-health
                        [health]="source.enabled ? source.health : 'disabled'"
                      />
                      @if (source.healthReason) {
                        · {{ source.healthReason }}
                      }
                    </small>
                  </span>
                  @if (source.lastSuccessAt) {
                    <time
                      class="end"
                      [attr.datetime]="source.lastSuccessAt"
                      [title]="dates.dateTime(source.lastSuccessAt)"
                      >{{ dates.relative(source.lastSuccessAt) }}</time
                    >
                  }
                </a>
              </li>
            }
          </ul>
        }
      } @else {
        <p class="none">{{ 'overview.sources.none' | transloco }}</p>
      }
    </ui-card>
  `,
  styles:
    ROWS +
    `
    .summary {
      color: var(--muted);
    }
    .summary + .rows {
      margin-top: var(--space-sm);
    }
  `,
})
export class SourcesCard {
  readonly sources = input.required<readonly SourceStatus[]>();
  protected readonly dates = inject(Dates);
  protected readonly summary = computed(() => sourceSummary(this.sources()));
}
