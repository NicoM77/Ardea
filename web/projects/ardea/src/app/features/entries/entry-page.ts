import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { LucideChevronRight, LucideCompass, LucideExternalLink } from '@lucide/angular';
import {
  UiBackLink,
  UiButton,
  UiCard,
  UiEmptyState,
  UiLoader,
  UiNotice,
  UiPageHeader,
  UiPill,
} from 'ui';
import {
  getEntryResource,
  listEntryChangesResource,
  listEntrySourcesResource,
  type AffectedView,
} from '../../core/api/client';
import { problemMessageKey } from '../../core/api/problem';
import { Dates } from '../../core/format/dates';
import {
  affectedStatusLabel,
  CHANGE_LABELS,
  FIELD_LABELS,
  productName,
  sourceLabel,
  versionRange,
  webUrl,
} from './entry-detail-format';
import { KIND_LABELS, score, SEVERITY_LABELS, signalPills, STATE_LABELS } from './entry-format';

const OPERATOR_KINDS: Record<string, string> = {
  authority: 'sources.operatorKind.authority',
  mandated: 'sources.operatorKind.mandated',
  recognized: 'sources.operatorKind.recognized',
  vendor: 'sources.operatorKind.vendor',
  community: 'sources.operatorKind.community',
};

@Component({
  selector: 'ardea-entry-page',
  imports: [
    RouterLink,
    TranslocoPipe,
    LucideChevronRight,
    LucideCompass,
    LucideExternalLink,
    UiBackLink,
    UiButton,
    UiCard,
    UiEmptyState,
    UiLoader,
    UiNotice,
    UiPageHeader,
    UiPill,
  ],
  template: `
    <ui-back-link [label]="'entryDetail.back' | transloco" link="/entries" history />

    @if (detail.hasValue() && detail.value(); as d) {
      <ui-page-header [heading]="d.entry.title ?? d.entry.key" />
      <div class="facts">
        <span class="id">{{ d.entry.key }}</span>
        <span>{{ kindLabel(d.entry.kind) | transloco }}</span>
        @if (d.entry.state !== 'published') {
          <ui-pill>{{ stateLabel(d.entry.state) | transloco }}</ui-pill>
        }
        @for (pill of pills(d.entry); track pill.key) {
          <ui-pill [tone]="pill.tone">{{ pill.key | transloco }}</ui-pill>
        }
      </div>

      @if (d.redirectedFrom) {
        <ui-notice class="gap" tone="info">
          {{ 'entryDetail.redirected' | transloco: { from: d.redirectedFrom } }}
        </ui-notice>
      }

      <div class="grid">
        <div class="column">
          <ui-card [heading]="'entryDetail.about' | transloco">
            @if (d.summary || d.description) {
              @if (d.summary && d.summary !== d.description) {
                <p class="summary">{{ d.summary }}</p>
              }
              @if (d.description) {
                <p class="text">{{ d.description }}</p>
              }
            } @else {
              <p class="muted">{{ 'entryDetail.noDescription' | transloco }}</p>
            }
            @if (d.entry.aliases.length) {
              <dl class="pairs gap">
                <div>
                  <dt>{{ 'entryDetail.aliases' | transloco }}</dt>
                  <dd class="mono">{{ d.entry.aliases.join(', ') }}</dd>
                </div>
              </dl>
            }
          </ui-card>

          <ui-card [heading]="'entryDetail.affected' | transloco">
            <span uiCardMeta>{{
              'entryDetail.affectedCount' | transloco: { count: ranges().affected.length }
            }}</span>
            @if (ranges().affected.length || showUnaffected()) {
              <div class="table-wrap">
                <table [attr.aria-label]="'entryDetail.affected' | transloco">
                  <thead>
                    <tr>
                      <th scope="col">{{ 'entryDetail.product' | transloco }}</th>
                      <th scope="col">{{ 'entryDetail.versions' | transloco }}</th>
                      <th scope="col">{{ 'entryDetail.status' | transloco }}</th>
                      <th scope="col">{{ 'entryDetail.source' | transloco }}</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (a of shownRanges(); track $index) {
                      <tr>
                        <td>
                          {{ product(a) }}
                          @if (a.purl && product(a) !== a.purl) {
                            <span class="mono sub">{{ a.purl }}</span>
                          }
                        </td>
                        <td class="mono">
                          @let range = rangeOf(a);
                          {{ range.key | transloco: range.params }}
                        </td>
                        <td class="nowrap">{{ affectedStatus(a.status) | transloco }}</td>
                        <td class="nowrap">
                          @let from = source(a.source);
                          {{ from.key ? (from.key | transloco) : from.name }}
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            } @else {
              <p class="muted">{{ 'entryDetail.noAffected' | transloco }}</p>
            }
            @if (ranges().unaffected.length) {
              <p class="more">
                <button
                  uiButton
                  variant="ghost"
                  type="button"
                  [attr.aria-expanded]="showUnaffected()"
                  (click)="showUnaffected.set(!showUnaffected())"
                >
                  {{
                    (showUnaffected() ? 'entryDetail.hideUnaffected' : 'entryDetail.showUnaffected')
                      | transloco: { count: ranges().unaffected.length }
                  }}
                </button>
              </p>
            }
          </ui-card>

          @if (d.solutions.length || d.workarounds.length) {
            <ui-card [heading]="'entryDetail.remedies' | transloco">
              @if (d.solutions.length) {
                <h3>{{ 'entryDetail.solutions' | transloco }}</h3>
                <ul class="list">
                  @for (s of d.solutions; track $index) {
                    <li>{{ s }}</li>
                  }
                </ul>
              }
              @if (d.workarounds.length) {
                <h3 class="gap">{{ 'entryDetail.workarounds' | transloco }}</h3>
                <ul class="list">
                  @for (w of d.workarounds; track $index) {
                    <li>{{ w }}</li>
                  }
                </ul>
              }
            </ui-card>
          }

          <ui-card [heading]="'entryDetail.references' | transloco">
            @if (d.references.length) {
              <ul class="list">
                @for (r of d.references; track r.url) {
                  <li>
                    @if (web(r.url); as href) {
                      <a class="link ext" [href]="href" target="_blank" rel="noopener noreferrer">
                        {{ r.url }}<svg lucideExternalLink aria-hidden="true"></svg>
                      </a>
                    } @else {
                      <span class="mono">{{ r.url }}</span>
                    }
                    @if (r.tags.length) {
                      <span class="sub">{{ r.tags.join(', ') }}</span>
                    }
                  </li>
                }
              </ul>
            } @else {
              <p class="muted">{{ 'entryDetail.noReferences' | transloco }}</p>
            }
          </ui-card>
        </div>

        <div class="column">
          <ui-card [heading]="'entryDetail.severity' | transloco">
            <p class="score">
              @if (score(d.entry.cvssScore); as value) {
                <b>{{ value }}</b>
              }
              {{ severityLabel(d.entry.severity) | transloco }}
              @if (d.cvssVersion) {
                <span class="muted">· CVSS {{ d.cvssVersion }}</span>
              }
            </p>
            @if (d.cvssVector) {
              <p class="vector mono">{{ d.cvssVector }}</p>
            }
            @if (d.metrics.length > 1) {
              <dl class="pairs gap">
                @for (m of d.metrics; track $index) {
                  <div>
                    <dt>
                      {{ m.system }} {{ m.version }}
                      @if (m.assigner) {
                        · {{ m.assigner }}
                      }
                    </dt>
                    <dd>
                      @if (score(m.score); as value) {
                        <b>{{ value }}</b>
                      }
                      @if (m.vector) {
                        <span class="mono small">{{ m.vector }}</span>
                      }
                    </dd>
                  </div>
                }
              </dl>
            }
            @if (d.cweIds.length) {
              <dl class="pairs gap">
                <div>
                  <dt>{{ 'entryDetail.weaknesses' | transloco }}</dt>
                  <dd class="mono">{{ d.cweIds.join(', ') }}</dd>
                </div>
              </dl>
            }
          </ui-card>

          <ui-card [heading]="'entryDetail.exploitation' | transloco">
            @if (d.exploitationEvidence.length) {
              <ul class="list">
                @for (e of d.exploitationEvidence; track $index) {
                  <li>
                    <span>
                      @let from = source(e.source);
                      {{ from.key ? (from.key | transloco) : from.name }} · {{ e.state }}
                      @if (e.since) {
                        · <time [attr.datetime]="e.since">{{ when(e.since) }}</time>
                      }
                    </span>
                    @if (web(e.evidence); as href) {
                      <a
                        class="link ext sub"
                        [href]="href"
                        target="_blank"
                        rel="noopener noreferrer"
                      >
                        {{ 'entryDetail.evidence' | transloco
                        }}<svg lucideExternalLink aria-hidden="true"></svg>
                      </a>
                    }
                  </li>
                }
              </ul>
            } @else {
              <p class="muted">{{ 'entryDetail.noExploitation' | transloco }}</p>
            }
          </ui-card>

          <ui-card [heading]="'entryDetail.sources' | transloco">
            @if (sources.hasValue() && sources.value(); as list) {
              <ul class="list">
                @for (s of list; track s.source + s.relation + s.url) {
                  <li>
                    @if (web(s.url); as href) {
                      <a class="link ext" [href]="href" target="_blank" rel="noopener noreferrer">
                        {{ s.name }}<svg lucideExternalLink aria-hidden="true"></svg>
                      </a>
                    } @else {
                      <b>{{ s.name }}</b>
                    }
                    <span class="sub">
                      {{ s.operator }} · {{ operatorKind(s.operatorKind) | transloco }} ·
                      {{ s.relation }}
                    </span>
                    <span class="sub small">{{ s.attribution }}</span>
                  </li>
                }
              </ul>
              @if (provenance().length) {
                <details class="fields">
                  <summary>
                    <span>{{ 'entryDetail.provenance' | transloco }}</span>
                    <svg class="chevron" lucideChevronRight aria-hidden="true"></svg>
                  </summary>
                  <dl class="pairs gap">
                    @for (p of provenance(); track p.field) {
                      <div>
                        <dt>{{ p.label | transloco }}</dt>
                        <dd>{{ p.source.key ? (p.source.key | transloco) : p.source.name }}</dd>
                      </div>
                    }
                  </dl>
                </details>
              }
            } @else if (sources.error()) {
              <p class="muted">{{ 'entryDetail.sourcesFailed' | transloco }}</p>
            } @else {
              <ui-loader [label]="'entryDetail.loadingPart' | transloco" />
            }
          </ui-card>

          <ui-card [heading]="'entryDetail.history' | transloco">
            <span uiCardMeta>{{
              'entryDetail.revision' | transloco: { revision: d.revision }
            }}</span>
            @if (changes.hasValue() && changes.value(); as list) {
              @if (list.length) {
                <ol class="history">
                  @for (c of list; track c.revision) {
                    <li>
                      <time [attr.datetime]="c.changedAt">{{ when(c.changedAt) }}</time>
                      <span>
                        {{ 'entryDetail.revision' | transloco: { revision: c.revision } }}
                        @if (c.isMaterial) {
                          <ui-pill>{{ 'entryDetail.material' | transloco }}</ui-pill>
                        }
                      </span>
                      @if (c.kinds.length) {
                        <span class="sub">{{ changeKinds(c.kinds) }}</span>
                      }
                      @if (c.triggeredBy) {
                        @let from = source(c.triggeredBy);
                        <span class="sub">{{
                          'entryDetail.triggeredBy'
                            | transloco: { source: from.key ? (from.key | transloco) : from.name }
                        }}</span>
                      }
                    </li>
                  }
                </ol>
              } @else {
                <p class="muted">{{ 'entryDetail.noChanges' | transloco }}</p>
              }
            } @else if (changes.error()) {
              <p class="muted">{{ 'entryDetail.changesFailed' | transloco }}</p>
            } @else {
              <ui-loader [label]="'entryDetail.loadingPart' | transloco" />
            }
          </ui-card>
        </div>
      </div>
    } @else if (notFound()) {
      <ui-page-header [heading]="'entryDetail.notFound.title' | transloco" />
      <ui-empty-state [heading]="'entryDetail.notFound.heading' | transloco: { key: key() }">
        <svg uiEmptyIcon lucideCompass aria-hidden="true"></svg>
        {{ 'entryDetail.notFound.text' | transloco }}
        <a uiEmptyActions uiButton routerLink="/entries">{{ 'entryDetail.back' | transloco }}</a>
      </ui-empty-state>
    } @else if (detail.error()) {
      <ui-page-header [heading]="key()" />
      <ui-notice tone="error">{{ problem() | transloco }}</ui-notice>
      <div class="gap">
        <button uiButton type="button" (click)="detail.reload()">
          {{ 'entries.tryAgain' | transloco }}
        </button>
      </div>
    } @else {
      <ui-page-header [heading]="key()" />
      <div class="wait" aria-busy="true">
        <ui-loader size="page" [label]="'entryDetail.loading' | transloco" />
      </div>
    }
  `,
  styles: `
    .facts {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-xs) var(--space-md);
      margin: calc(-1 * var(--space-md)) 0 var(--gap);
      color: var(--muted);
    }
    .grid {
      display: grid;
      grid-template-columns: minmax(0, 7fr) minmax(0, 5fr);
      gap: var(--gap);
      align-items: start;
      margin-top: var(--gap);
    }
    .column {
      display: grid;
      gap: var(--gap);
      min-width: 0;
    }
    .gap {
      display: block;
      margin-top: var(--space-md);
    }
    .wait {
      display: grid;
      place-items: center;
      min-height: 280px;
    }
    .muted {
      color: var(--muted);
    }
    .small {
      font-size: var(--fs-small);
      line-height: var(--lh-small);
    }
    .mono {
      font-family: var(--mono);
      font-size: var(--fs-sub);
      overflow-wrap: anywhere;
    }
    .summary {
      font-weight: 500;
      margin-bottom: var(--space-sm);
    }
    .text {
      white-space: pre-line;
      overflow-wrap: anywhere;
      max-width: 75ch;
    }
    h3 {
      margin-bottom: var(--space-xs);
    }
    .list {
      display: grid;
      gap: var(--space-sm);
    }
    .list li {
      display: grid;
      gap: 2px;
      min-width: 0;
    }
    .sub {
      display: block;
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .ext {
      display: inline-flex;
      align-items: center;
      gap: var(--space-xs);
      overflow-wrap: anywhere;
    }
    .ext svg {
      width: 12px;
      height: 12px;
      flex: none;
    }
    .pairs {
      display: grid;
      gap: var(--space-sm);
    }
    .pairs > div {
      display: grid;
      grid-template-columns: minmax(0, 2fr) minmax(0, 3fr);
      gap: var(--space-md);
      padding-top: var(--space-sm);
      border-top: 1px solid var(--rule);
    }
    .pairs dt {
      color: var(--muted);
    }
    .pairs dd {
      display: grid;
      gap: 2px;
      min-width: 0;
    }
    .score {
      font-size: var(--fs-section);
      line-height: var(--lh-section);
    }
    .score b {
      font-size: var(--fs-kpi);
      line-height: var(--lh-kpi);
      font-weight: 500;
      margin-right: var(--space-xs);
    }
    .vector {
      margin-top: var(--space-sm);
      padding: var(--space-sm) 10px;
      border-radius: var(--radius-control);
      background: var(--raised);
    }
    .table-wrap {
      overflow-x: auto;
    }
    table {
      width: 100%;
      border-collapse: collapse;
    }
    th {
      height: 32px;
      padding: 0 var(--space-sm) 0 0;
      color: var(--muted);
      font-size: var(--fs-small);
      font-weight: 400;
      text-align: left;
      white-space: nowrap;
    }
    td {
      padding: var(--space-sm) var(--space-sm) var(--space-sm) 0;
      border-top: 1px solid var(--rule);
      vertical-align: top;
      overflow-wrap: anywhere;
    }
    .history {
      display: grid;
      gap: var(--space-md);
    }
    .history li {
      display: grid;
      gap: 2px;
      padding-left: var(--space-md);
      border-left: 2px solid var(--rule);
    }
    .history time {
      color: var(--muted);
      font-size: var(--fs-sub);
    }
    .more {
      margin-top: var(--space-md);
    }
    .nowrap {
      white-space: nowrap;
      overflow-wrap: normal;
    }
    .fields {
      margin-top: var(--space-md);
      border-top: 1px solid var(--rule);
    }
    .fields summary {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-md);
      min-height: 40px;
      padding-top: var(--space-md);
      color: var(--muted);
      cursor: pointer;
      list-style: none;
    }
    .fields summary::-webkit-details-marker {
      display: none;
    }
    .fields summary:focus-visible {
      outline: var(--focus-ring);
      outline-offset: var(--focus-offset);
      border-radius: var(--radius-control);
    }
    .chevron {
      width: 16px;
      height: 16px;
      color: var(--faint);
      transition: transform var(--t-ui) var(--ease-out);
    }
    .fields[open] .chevron {
      transform: rotate(90deg);
    }
    @media (hover: hover) and (pointer: fine) {
      .fields summary:hover {
        color: var(--text);
      }
    }
    @media (prefers-reduced-motion: reduce) {
      .chevron {
        transition: none;
      }
    }
    @media (max-width: 1100px) {
      .grid {
        grid-template-columns: minmax(0, 1fr);
      }
    }
  `,
})
export class EntryPage {
  readonly key = input.required<string>();

  private readonly transloco = inject(TranslocoService);
  private readonly dates = inject(Dates);

  private readonly keySignal = computed(() => this.key());
  protected readonly detail = getEntryResource(this.keySignal);
  protected readonly changes = listEntryChangesResource(this.keySignal);
  protected readonly sources = listEntrySourcesResource(this.keySignal);

  protected readonly notFound = computed(() => {
    const error = this.detail.error();
    return error instanceof HttpErrorResponse && error.status === 404;
  });
  protected readonly problem = computed(() =>
    problemMessageKey(this.detail.error(), { fallback: 'entryDetail.loadFailed' }),
  );
  private readonly sourceNames = computed(
    () =>
      new Map(
        (this.sources.hasValue() ? (this.sources.value() ?? []) : []).map((s) => [
          s.source,
          s.name,
        ]),
      ),
  );
  protected readonly provenance = computed(() => {
    const value = this.detail.hasValue() ? this.detail.value()?.provenance : undefined;
    const order = Object.keys(FIELD_LABELS);
    return Object.entries(value ?? {})
      .filter(([field]) => field in FIELD_LABELS)
      .sort(([a], [b]) => order.indexOf(a) - order.indexOf(b))
      .map(([field, source]) => ({
        field,
        label: FIELD_LABELS[field] ?? field,
        source: sourceLabel(source, this.sourceNames()),
      }));
  });
  protected readonly showUnaffected = signal(false);
  protected readonly ranges = computed(() => {
    const all: AffectedView[] = this.detail.hasValue() ? (this.detail.value()?.affected ?? []) : [];
    return {
      affected: all.filter((a) => a.status !== 'unaffected'),
      unaffected: all.filter((a) => a.status === 'unaffected'),
    };
  });
  protected readonly shownRanges = computed(() =>
    this.showUnaffected()
      ? [...this.ranges().affected, ...this.ranges().unaffected]
      : this.ranges().affected,
  );
  protected readonly affectedStatus = affectedStatusLabel;

  protected readonly score = score;
  protected readonly pills = signalPills;
  protected readonly rangeOf = versionRange;
  protected readonly product = productName;
  protected readonly web = webUrl;

  protected severityLabel(severity: string): string {
    return SEVERITY_LABELS[severity] ?? 'entries.severity.unrated';
  }

  protected stateLabel(state: string): string {
    return STATE_LABELS[state] ?? 'entries.state.published';
  }

  protected kindLabel(kind: string): string {
    return KIND_LABELS[kind] ?? 'entries.kind.vulnerability';
  }

  protected source(value: string) {
    return sourceLabel(value, this.sourceNames());
  }

  protected changeKinds(kinds: readonly string[]): string {
    return kinds
      .map((kind) => (CHANGE_LABELS[kind] ? this.transloco.translate(CHANGE_LABELS[kind]) : kind))
      .join(', ');
  }

  protected operatorKind(kind: string): string {
    return OPERATOR_KINDS[kind] ?? 'sources.operatorKind.community';
  }

  protected when(value: string): string {
    return this.dates.dateTime(value);
  }
}
