import { Component, computed, inject, input, linkedSignal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { LucideChevronLeft, LucideChevronRight } from '@lucide/angular';
import { UiButton, UiCard, UiLoader, UiNotice, UiPill, UiPriority } from 'ui';
import {
  getAlertResource,
  listEntrySourcesResource,
  type FindingView,
} from '../../core/api/client';
import { problemMessageKey } from '../../core/api/problem';
import { reloadOnLiveChange } from '../../core/live/live-events';
import { score } from '../entries/entry-format';
import { injectOrganization } from '../organizations/current-organization';
import { OPERATOR_KIND_LABELS } from '../sources/source-format';
import { num, priorityOf } from './action-format';
import { checkLabel, reasonLabel } from './answers-format';
import { Answers } from './answers';
import { FactorTable } from './factor-table';

/**
 * An alert (task 3.10, master plan 7.4, mockup alert.html): one vulnerability from the
 * organization's view. The three answers, every component it hits with its own version check,
 * level and factors, the action each belongs to, and the entry's facts and sources.
 */
@Component({
  selector: 'ardea-alert-page',
  imports: [
    RouterLink,
    TranslocoPipe,
    LucideChevronLeft,
    LucideChevronRight,
    UiButton,
    UiCard,
    UiLoader,
    UiNotice,
    UiPill,
    UiPriority,
    Answers,
    FactorTable,
  ],
  template: `
    @if (view(); as v) {
      @if (v.actions[0]; as first) {
        <a class="crumb" [routerLink]="['../../actions', actionNumber(first.number)]">
          <svg lucideChevronLeft aria-hidden="true"></svg
          >{{ 'alert.back' | transloco: { number: actionNumber(first.number) } }}
        </a>
      } @else {
        <a class="crumb" routerLink="../../actions">
          <svg lucideChevronLeft aria-hidden="true"></svg>{{ 'actions.title' | transloco }}
        </a>
      }
      <header class="head">
        <h1>{{ v.entry.title ?? v.entry.key }}</h1>
        <p class="meta">
          <ui-priority [level]="priority(v.alert.levelName)" />
          <span class="id">#{{ actionNumber(v.alert.number) }}</span>
          <span class="id">{{ v.entry.key }}</span>
          @if (cvss(); as s) {
            <span>CVSS {{ s }}</span>
          }
          @if (v.entry.exploitation === 'active') {
            <ui-pill tone="alert">{{ 'actions.pill.exploited' | transloco }}</ui-pill>
          }
          @if (v.entry.fixAvailable) {
            <ui-pill tone="done">{{ 'actions.pill.fix' | transloco }}</ui-pill>
          }
        </p>
      </header>

      <div class="grid">
        <div class="stack">
          <section class="answers-card" [attr.aria-label]="'answers.label' | transloco">
            @if (v.answers; as answers) {
              <ardea-answers
                [answers]="answers"
                [component]="answers.affected.items[0]?.component?.name ?? null"
              />
            } @else {
              <p class="none">{{ 'answers.none' | transloco }}</p>
            }
          </section>

          <ui-card [heading]="'alert.hits.title' | transloco" headingId="alert-hits">
            <span uiCardMeta>{{
              'alert.hits.meta' | transloco: { count: v.findings.length }
            }}</span>
            <table aria-labelledby="alert-hits">
              <thead>
                <tr>
                  <th scope="col">{{ 'actions.table.priority' | transloco }}</th>
                  <th scope="col">{{ 'alert.hits.component' | transloco }}</th>
                  <th scope="col">{{ 'alert.hits.check' | transloco }}</th>
                  <th scope="col">{{ 'alert.hits.action' | transloco }}</th>
                  <th scope="col">
                    <span class="sr-only">{{ 'alert.hits.factors' | transloco }}</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                @for (f of v.findings; track f.id) {
                  <tr>
                    <td><ui-priority [level]="priority(f.levelName)" /></td>
                    <td>
                      <b
                        >{{ f.component.name }}
                        @if (f.component.version) {
                          <span class="id">{{ f.component.version }}</span>
                        }
                      </b>
                      <small
                        >{{ f.project.name }} ·
                        {{ 'alert.hits.match' | transloco: { type: f.matchType } }}</small
                      >
                    </td>
                    <td>
                      {{ check(f.versionVerdict) | transloco }}
                      @if (reason(f); as why) {
                        <small>{{ why | transloco }}</small>
                      }
                    </td>
                    <td>
                      @if (f.actionNumber !== null) {
                        <a
                          class="link"
                          [routerLink]="['../../actions', actionNumber(f.actionNumber)]"
                          >#{{ actionNumber(f.actionNumber) }}</a
                        >
                      } @else {
                        <small>{{ 'alert.hits.noAction' | transloco }}</small>
                      }
                    </td>
                    <td class="toggle">
                      <button
                        uiButton
                        variant="ghost"
                        type="button"
                        [attr.aria-expanded]="open() === f.id"
                        [attr.aria-controls]="'factors-' + f.id"
                        (click)="open.set(open() === f.id ? null : f.id)"
                      >
                        {{ 'alert.hits.showFactors' | transloco }}
                        <svg
                          lucideChevronRight
                          aria-hidden="true"
                          [class.turned]="open() === f.id"
                        ></svg>
                      </button>
                    </td>
                  </tr>
                  @if (open() === f.id) {
                    <tr class="factors" [id]="'factors-' + f.id">
                      <td colspan="5">
                        <ardea-factor-table [factors]="f.factors" [caption]="factorsCaption(f)" />
                      </td>
                    </tr>
                  }
                }
              </tbody>
            </table>
          </ui-card>
        </div>

        <div class="stack">
          <ui-card [heading]="'alert.entry.title' | transloco">
            <a uiCardMeta class="link" [routerLink]="['/entries', v.entry.key]">{{
              'alert.entry.full' | transloco
            }}</a>
            <dl class="facts">
              <div>
                <dt>{{ 'alert.entry.severity' | transloco }}</dt>
                <dd>
                  @if (cvss(); as s) {
                    CVSS {{ v.entry.cvssVersion }} · {{ s }}
                  } @else {
                    {{ 'alert.entry.unrated' | transloco }}
                  }
                </dd>
              </div>
              <div>
                <dt>{{ 'alert.entry.exploitation' | transloco }}</dt>
                <dd>
                  {{ 'alert.entry.exploitationValue' | transloco: { value: v.entry.exploitation } }}
                </dd>
              </div>
              <div>
                <dt>{{ 'alert.entry.epss' | transloco }}</dt>
                <dd>
                  {{ 'alert.entry.epssValue' | transloco: { value: v.entry.epssClass ?? 'none' } }}
                </dd>
              </div>
              <div>
                <dt>{{ 'alert.entry.warning' | transloco }}</dt>
                <dd>
                  {{
                    (v.entry.authorityWarning ? 'alert.entry.yes' : 'alert.entry.no') | transloco
                  }}
                </dd>
              </div>
              @if (v.entry.publishedAt) {
                <div>
                  <dt>{{ 'alert.entry.published' | transloco }}</dt>
                  <dd>{{ when(v.entry.publishedAt) }}</dd>
                </div>
              }
              <div>
                <dt>{{ 'alert.entry.changed' | transloco }}</dt>
                <dd>{{ when(v.entry.changedAt) }}</dd>
              </div>
              @if (v.entry.cvssVector) {
                <div class="wide">
                  <dt>{{ 'alert.entry.vector' | transloco }}</dt>
                  <dd class="vector">{{ v.entry.cvssVector }}</dd>
                </div>
              }
            </dl>
          </ui-card>
          <ui-card [heading]="'alert.sources.title' | transloco">
            <ul class="sources">
              @for (s of sources.value() ?? []; track s.source) {
                <li>
                  <b>
                    @if (s.url) {
                      <a class="link" [href]="s.url" target="_blank" rel="noopener noreferrer">{{
                        s.name
                      }}</a>
                    } @else {
                      {{ s.name }}
                    }
                  </b>
                  <small>{{ s.operator }} · {{ operatorKind(s.operatorKind) | transloco }}</small>
                </li>
              } @empty {
                <li>
                  <small>{{ 'alert.sources.none' | transloco }}</small>
                </li>
              }
            </ul>
          </ui-card>
        </div>
      </div>
    } @else if (detail.error()) {
      <ui-notice tone="error">{{ loadProblem() | transloco }}</ui-notice>
    } @else {
      <div class="wait"><ui-loader size="page" [label]="'alert.loading' | transloco" /></div>
    }
  `,
  styles: `
    .crumb {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      height: 24px;
      margin: -4px 0 var(--space-sm) -4px;
      padding: 0 6px 0 2px;
      color: var(--muted);
    }
    .head {
      margin-bottom: var(--space-xl);
    }
    .meta {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 6px var(--space-md);
      margin-top: var(--space-sm);
      color: var(--muted);
    }
    .grid {
      display: grid;
      grid-template-columns: minmax(0, 8fr) minmax(0, 4fr);
      gap: var(--gap);
      align-items: start;
    }
    .stack {
      display: grid;
      gap: var(--gap);
      min-width: 0;
    }
    .answers-card {
      border: 1px solid var(--border);
      border-radius: var(--radius-card);
    }
    .none {
      padding: var(--card-pad);
      color: var(--muted);
    }
    table {
      width: 100%;
      border-collapse: collapse;
    }
    th {
      height: 32px;
      padding-right: var(--space-md);
      color: var(--muted);
      font-size: var(--fs-small);
      font-weight: 400;
      text-align: left;
    }
    td {
      padding: 10px var(--space-md) 10px 0;
      border-top: 1px solid var(--rule);
      vertical-align: top;
    }
    td b {
      display: block;
      font-weight: 500;
    }
    td small,
    .sources small {
      display: block;
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .toggle {
      padding-right: 0;
      text-align: right;
    }
    .turned {
      transform: rotate(90deg);
    }
    .factors td {
      padding-top: 0;
      border-top: 0;
    }
    .facts {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--space-md) var(--gap);
    }
    .facts .wide {
      grid-column: 1 / -1;
    }
    dt {
      color: var(--muted);
      font-size: var(--fs-sub);
    }
    dd {
      margin-top: 2px;
      font-weight: 500;
      overflow-wrap: anywhere;
    }
    .vector {
      font: 400 var(--fs-sub) / var(--lh-sub) var(--mono);
    }
    .sources li + li {
      margin-top: var(--space-md);
    }
    .wait {
      display: grid;
      place-items: center;
      min-height: 320px;
    }
    @media (max-width: 1100px) {
      .grid {
        grid-template-columns: minmax(0, 1fr);
      }
    }
    @media (max-width: 640px) {
      thead {
        display: none;
      }
      table,
      tbody,
      tr,
      td {
        display: block;
      }
      tr {
        padding: 10px 0;
        border-top: 1px solid var(--rule);
      }
      td {
        padding: 2px 0;
        border: 0;
      }
      .toggle {
        text-align: left;
      }
      .facts {
        grid-template-columns: minmax(0, 1fr);
      }
    }
  `,
})
export class AlertPage {
  /** From the route. */
  readonly number = input.required<string>();

  private readonly transloco = inject(TranslocoService);
  private readonly organization = injectOrganization().organization;
  protected readonly detail = getAlertResource(
    computed(() => this.organization()?.id),
    computed(() => num(this.number()) || undefined),
  );
  protected readonly view = computed(() =>
    this.detail.hasValue() ? (this.detail.value() ?? null) : null,
  );
  protected readonly sources = listEntrySourcesResource(computed(() => this.view()?.entry.key));
  protected readonly cvss = computed(() => score(this.view()?.entry.cvssScore ?? null));
  /** The finding whose factors are open; the first by default (mockup). */
  protected readonly open = linkedSignal<string | null>(() => this.view()?.findings[0]?.id ?? null);
  protected readonly loadProblem = computed(() =>
    problemMessageKey(this.detail.error(), {
      statuses: { 404: 'alert.gone' },
      fallback: 'alert.loadFailed',
    }),
  );

  constructor() {
    reloadOnLiveChange(this.detail);
  }

  protected readonly priority = priorityOf;
  protected readonly check = checkLabel;
  protected readonly actionNumber = num;

  protected reason(f: FindingView): string | null {
    return f.versionVerdict === 'unknown' ? reasonLabel(f.versionReason) : null;
  }

  protected operatorKind(kind: string): string {
    return OPERATOR_KIND_LABELS[kind] ?? 'sources.operatorKind.community';
  }

  protected factorsCaption(f: FindingView): string {
    return this.transloco.translate('alert.hits.factorsOf', {
      component: f.component.name,
      project: f.project.name,
    });
  }

  protected when(value: string): string {
    return new Intl.DateTimeFormat(this.transloco.getActiveLang(), {
      dateStyle: 'medium',
      timeStyle: 'short',
    }).format(new Date(value));
  }
}
