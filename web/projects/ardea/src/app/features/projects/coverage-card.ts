import { HttpResourceRef } from '@angular/common/http';
import { Component, computed, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { UiCard, UiLoader, UiNotice } from 'ui';
import type { CoverageHint, CoverageView } from '../../core/api/client';
import { ecosystemName } from './component-format';

const COVERAGE_LABELS: Record<string, string> = {
  missing_version: 'projects.coverage.kinds.missingVersion',
  version_line_only: 'projects.coverage.kinds.versionLineOnly',
  platform_without_plugins: 'projects.coverage.kinds.platformWithoutPlugins',
  distribution_packages: 'projects.coverage.kinds.distributionPackages',
  packages_without_lockfile: 'projects.coverage.kinds.packagesWithoutLockfile',
  ecosystem_not_checkable: 'projects.coverage.kinds.ecosystemNotCheckable',
  not_in_catalog: 'projects.coverage.kinds.notInCatalog',
  open_suggestions: 'projects.coverage.kinds.openSuggestions',
  repositories_partial: 'projects.coverage.kinds.repositoriesPartial',
  repositories_failing: 'projects.coverage.kinds.repositoriesFailing',
};

const number = (value: number | string) =>
  typeof value === 'number' ? value : Number.parseInt(value, 10) || 0;

@Component({
  selector: 'ardea-coverage-card',
  imports: [TranslocoPipe, UiCard, UiLoader, UiNotice],
  template: `
    <ui-card
      [heading]="'projects.coverage.title' | transloco"
      [attr.aria-busy]="coverage().isLoading()"
    >
      @if (view(); as c) {
        <div class="figure">
          <b>{{ checkable() }}</b>
          <span>{{ 'projects.coverage.figure' | transloco: { total: total() } }}</span>
        </div>
        <div class="bar" aria-hidden="true">
          <i [style.transform]="'scaleX(' + share() + ')'"></i>
        </div>
        @if (hints().length) {
          <ul class="hints">
            @for (hint of hints(); track $index) {
              <li>
                <span class="n">{{ count(hint.count) }}</span>
                <div>
                  <b>{{
                    labels[hint.kind] ?? hint.kind
                      | transloco: { count: count(hint.count), name: ecosystem(hint) }
                  }}</b>
                  @if (examples(hint); as names) {
                    <small>{{ names }}</small>
                  }
                </div>
              </li>
            }
          </ul>
        } @else {
          <p class="muted">{{ 'projects.coverage.none' | transloco }}</p>
        }
      } @else if (coverage().error()) {
        <ui-notice tone="error">{{ 'projects.coverage.loadFailed' | transloco }}</ui-notice>
      } @else {
        <ui-loader [label]="'projects.coverage.loading' | transloco" />
      }
    </ui-card>
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }
    .figure {
      display: flex;
      align-items: baseline;
      gap: var(--space-sm);
    }
    .figure b {
      font-size: var(--fs-kpi);
      font-weight: 600;
      line-height: 32px;
    }
    .figure span,
    .muted,
    small {
      color: var(--muted);
    }
    .bar {
      height: 4px;
      margin: var(--space-sm) 0 var(--space-lg);
      border-radius: 2px;
      background: var(--rule);
      overflow: hidden;
    }
    .bar i {
      display: block;
      height: 100%;
      background: var(--accent);
      transform-origin: left;
    }
    .hints li {
      display: grid;
      grid-template-columns: 44px minmax(0, 1fr);
      gap: var(--space-md);
      padding: var(--space-md) 0;
      border-top: 1px solid var(--rule);
    }
    .hints li:first-child {
      border-top: 0;
      padding-top: 0;
    }
    .n {
      font-size: var(--fs-section);
      font-weight: 600;
      text-align: right;
    }
    b {
      font-weight: 500;
    }
    small {
      display: block;
      margin-top: 2px;
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
      overflow-wrap: anywhere;
    }
  `,
})
export class CoverageCard {
  readonly coverage = input.required<HttpResourceRef<CoverageView | undefined>>();

  protected readonly labels = COVERAGE_LABELS;
  protected readonly count = number;
  protected readonly view = computed(() => {
    const c = this.coverage();
    return c.hasValue() ? (c.value() ?? null) : null;
  });
  protected readonly total = computed(() => number(this.view()?.components ?? 0));
  protected readonly checkable = computed(() => number(this.view()?.fullyCheckable ?? 0));
  protected readonly share = computed(() => (this.total() ? this.checkable() / this.total() : 1));
  protected readonly hints = computed(() =>
    [...(this.view()?.hints ?? [])].sort(
      (a, b) =>
        Number(b.severity === 'warning') - Number(a.severity === 'warning') ||
        number(b.count) - number(a.count),
    ),
  );

  protected ecosystem(hint: CoverageHint): string {
    const detail = hint.examples[0]?.detail;
    return hint.kind === 'ecosystem_not_checkable' && detail ? ecosystemName(detail) : '';
  }

  protected examples(hint: CoverageHint): string {
    if (hint.kind === 'ecosystem_not_checkable') return '';
    const names = hint.examples.slice(0, 3).map((e) => e.name);
    return names.join(', ') + (number(hint.count) > names.length ? ', …' : '');
  }
}
