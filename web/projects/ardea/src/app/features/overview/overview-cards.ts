import { Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { LucideChevronRight, LucideMegaphone } from '@lucide/angular';
import { UiCard, type UiPriorityLevel } from 'ui';
import type { ChangeCountView, EntrySummary, SourceStatus } from '../../core/api/client';
import { num } from '../actions/action-format';
import { SourceHealth } from '../sources/source-health';
import { warningSource } from './overview-format';

/** Shared row styles of the overview's list cards (mockup `.rows`). */
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

function formatWhen(lang: string, value: string): string {
  return new Intl.DateTimeFormat(lang, { dateStyle: 'medium', timeStyle: 'short' }).format(
    new Date(value),
  );
}

/** Open actions per project (mockup «Projects»): one block per action in its priority's colour. */
@Component({
  selector: 'ardea-projects-card',
  imports: [RouterLink, TranslocoPipe, UiCard],
  template: `
    <ui-card [heading]="'overview.projects.title' | transloco">
      <ul class="rows">
        @for (p of projects(); track p.id) {
          <li>
            <a routerLink="../actions" [queryParams]="{ project: p.id }">
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

/** The words for each kind of change in the last 24 hours. */
const CHANGE_LABELS: Readonly<Record<string, string>> = {
  'action.raised': 'overview.changes.raised',
  'action.level_raised': 'overview.changes.levelRaised',
  'action.level_lowered': 'overview.changes.levelLowered',
  'action.exploitation_escalated': 'overview.changes.exploited',
  'action.fix_available': 'overview.changes.fixAvailable',
  'action.withdrawn': 'overview.changes.withdrawn',
  'action.assigned': 'overview.changes.assigned',
  'action.status_changed': 'overview.changes.status',
  'action.note_added': 'overview.changes.notes',
  'backfill.completed': 'overview.changes.backfill',
};

/**
 * What changed since yesterday (mockup «Since yesterday»), as counts per kind of change: the API
 * gives the counts of the last 24 hours, not the single events.
 */
@Component({
  selector: 'ardea-changes-card',
  imports: [TranslocoPipe, UiCard],
  template: `
    <ui-card [heading]="'overview.changes.title' | transloco">
      <span uiCardMeta>{{ 'overview.changes.meta' | transloco: { count: total() } }}</span>
      <ul class="rows">
        @for (c of known(); track c.kind) {
          <li>
            <span class="row">
              <span class="main"
                ><b>{{ c.label | transloco: { count: c.count } }}</b></span
              >
            </span>
          </li>
        } @empty {
          <li class="none">{{ 'overview.changes.none' | transloco }}</li>
        }
      </ul>
    </ui-card>
  `,
  styles: ROWS,
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

/**
 * Warnings without an identifier (mockup, master plan 11.4): campaigns and advisories of national
 * centres with no CVE yet, from the last seven days. They name no product of the organization, so
 * they raise no action; each leads to its entry.
 */
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
              <time class="end" [attr.datetime]="w.changedAt">{{
                when(w.publishedAt ?? w.changedAt)
              }}</time>
            </a>
          </li>
        } @empty {
          <li class="none">{{ 'overview.warnings.none' | transloco }}</li>
        }
      </ul>
    </ui-card>
  `,
  styles: ROWS,
})
export class WarningsCard {
  readonly warnings = input.required<readonly EntrySummary[]>();
  readonly sources = input<readonly SourceStatus[]>([]);
  private readonly transloco = inject(TranslocoService);

  protected source(key: string): string | null {
    return warningSource(key, this.sources());
  }

  protected when(value: string): string {
    return formatWhen(this.transloco.getActiveLang(), value);
  }
}

/** The state of the sources (mockup «Sources»): symbol and word; only Healthy takes a colour. */
@Component({
  selector: 'ardea-sources-card',
  imports: [RouterLink, TranslocoPipe, LucideChevronRight, UiCard, SourceHealth],
  template: `
    <ui-card [heading]="'overview.sources.title' | transloco">
      <a uiCardMeta class="card-link" routerLink="/sources"
        >{{ 'overview.sources.all' | transloco }}<svg lucideChevronRight aria-hidden="true"></svg
      ></a>
      <ul class="rows">
        @for (s of sources(); track s.key) {
          <li>
            <a routerLink="/sources">
              <span class="main">
                <b>{{ s.name }}</b>
                <small
                  ><ardea-source-health [health]="s.enabled ? s.health : 'disabled'" />
                  @if (s.healthReason) {
                    · {{ s.healthReason }}
                  }
                </small>
              </span>
              @if (s.lastSuccessAt) {
                <time class="end" [attr.datetime]="s.lastSuccessAt">{{
                  when(s.lastSuccessAt)
                }}</time>
              }
            </a>
          </li>
        } @empty {
          <li class="none">{{ 'overview.sources.none' | transloco }}</li>
        }
      </ul>
    </ui-card>
  `,
  styles: ROWS,
})
export class SourcesCard {
  readonly sources = input.required<readonly SourceStatus[]>();
  private readonly transloco = inject(TranslocoService);

  protected when(value: string): string {
    return formatWhen(this.transloco.getActiveLang(), value);
  }
}
