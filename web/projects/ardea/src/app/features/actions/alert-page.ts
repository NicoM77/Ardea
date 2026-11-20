import {
  Component,
  computed,
  inject,
  input,
  linkedSignal,
  signal,
  viewChildren,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { LucideChevronRight, LucideSparkles } from '@lucide/angular';
import { firstValueFrom } from 'rxjs';
import {
  UiBackLink,
  UiButton,
  UiCard,
  UiLoader,
  UiNotice,
  UiPill,
  UiPriority,
  UiToaster,
} from 'ui';
import {
  ActionsService,
  getAlertResource,
  listEntrySourcesResource,
  listMembersResource,
  type FindingView,
} from '../../core/api/client';
import { problemMessageKey, problemOf } from '../../core/api/problem';
import { reloadOnLiveChange } from '../../core/live/live-events';
import { webUrl } from '../entries/entry-detail-format';
import { score } from '../entries/entry-format';
import { injectOrganization } from '../organizations/current-organization';
import { roleRank } from '../organizations/org-format';
import { OPERATOR_KIND_LABELS } from '../sources/source-format';
import { num, priorityOf } from './action-format';
import { checkLabel, reasonLabel } from './answers-format';
import { AiAssessmentCard } from './ai-assessment-card';
import { Answers } from './answers';
import { LearnedNote } from './learned-note';
import { FactorTable } from './factor-table';
import { isProposal, isStaleReview, REQUEST_PROBLEMS, REVIEW_PROBLEMS } from './model-format';
import { ModelReview, type ReviewChange } from './model-review';

@Component({
  selector: 'ardea-alert-page',
  imports: [
    RouterLink,
    TranslocoPipe,
    LucideChevronRight,
    LucideSparkles,
    UiBackLink,
    UiButton,
    UiCard,
    UiLoader,
    UiNotice,
    UiPill,
    UiPriority,
    AiAssessmentCard,
    Answers,
    FactorTable,
    LearnedNote,
    ModelReview,
  ],
  template: `
    @if (view(); as v) {
      @if (v.actions[0]; as first) {
        <ui-back-link
          [label]="'alert.back' | transloco: { number: actionNumber(first.number) }"
          [link]="['../../reminders', actionNumber(first.number)]"
        />
      } @else {
        <ui-back-link [label]="'actions.title' | transloco" link="../../reminders" />
      }
      <header class="head">
        <div class="title">
          <h1>{{ v.entry.title ?? v.entry.key }}</h1>
          @if (canEdit() && v.assessmentAvailable) {
            <button uiButton type="button" [loading]="requesting()" (click)="request()">
              <svg lucideSparkles aria-hidden="true"></svg>
              {{ 'aiAssessment.request.button' | transloco }}
            </button>
          }
        </div>
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

      @if (problem(); as p) {
        <ui-notice class="problem" tone="error">{{ p | transloco }}</ui-notice>
      }

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
                      <ardea-learned-note [evidence]="f.evidence" />
                    </td>
                    <td>
                      {{ check(f.versionVerdict) | transloco }}
                      @if (reason(f); as why) {
                        <small>{{ why | transloco }}</small>
                      }
                      @if (f.verdictSource === 'model') {
                        <small class="model"
                          ><span class="ai-tag">{{
                            'answers.model.verdict' | transloco: { verdict: f.verdict }
                          }}</span>
                          @if (proposal(f)) {
                            {{ 'estimate.proposal' | transloco }}
                          }
                        </small>
                      }
                    </td>
                    <td>
                      @if (f.actionNumber !== null) {
                        <a
                          class="link"
                          [routerLink]="['../../reminders', actionNumber(f.actionNumber)]"
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
                        <ardea-model-review
                          [finding]="f"
                          [members]="members.value() ?? []"
                          [canEdit]="canEdit()"
                          [busy]="busy()"
                          (review)="review($event)"
                        />
                      </td>
                    </tr>
                  }
                }
              </tbody>
            </table>
          </ui-card>
          @if (v.answers && v.answers.origin === 'model' && v.answers.model; as model) {
            <ardea-ai-assessment-card
              [model]="model"
              [canRequest]="canEdit() && v.assessmentAvailable"
              [busy]="requesting()"
              (again)="request()"
            />
          }
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
                    @if (web(s.url); as href) {
                      <a class="link" [href]="href" target="_blank" rel="noopener noreferrer">{{
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
    .head {
      margin-bottom: var(--space-xl);
    }
    .title {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-start;
      justify-content: space-between;
      gap: var(--space-md) var(--gap);
    }
    .problem {
      margin-bottom: var(--space-lg);
    }
    .ai-tag {
      display: inline-flex;
      align-items: center;
      height: 18px;
      padding: 0 6px;
      border-radius: 4px;
      background: var(--accent-soft);
      color: var(--text);
      font-size: var(--fs-small);
      font-weight: 500;
      white-space: nowrap;
    }
    td small.model {
      margin-top: 4px;
      color: var(--text);
    }
    ardea-model-review {
      margin-top: var(--space-md);
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
  readonly number = input.required<string>();

  private readonly transloco = inject(TranslocoService);
  private readonly api = inject(ActionsService);
  private readonly toaster = inject(UiToaster);
  private readonly organization = injectOrganization().organization;
  private readonly orgId = computed(() => this.organization()?.id);
  private readonly reviews = viewChildren(ModelReview);
  protected readonly detail = getAlertResource(
    this.orgId,
    computed(() => num(this.number()) || undefined),
  );
  protected readonly members = listMembersResource(this.orgId);
  protected readonly canEdit = computed(
    () => roleRank(this.organization()?.role) >= roleRank('member'),
  );
  protected readonly busy = signal(false);
  protected readonly requesting = signal(false);
  protected readonly problem = signal<string | null>(null);
  protected readonly view = computed(() =>
    this.detail.hasValue() ? (this.detail.value() ?? null) : null,
  );
  protected readonly sources = listEntrySourcesResource(computed(() => this.view()?.entry.key));
  protected readonly cvss = computed(() => score(this.view()?.entry.cvssScore ?? null));
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
  protected readonly web = webUrl;
  protected readonly check = checkLabel;
  protected readonly actionNumber = num;
  protected readonly proposal = isProposal;

  protected async request(): Promise<void> {
    const org = this.orgId();
    if (!org || this.requesting()) return;
    this.requesting.set(true);
    this.problem.set(null);
    try {
      const answer = await firstValueFrom(this.api.requestAlertAssessment(org, num(this.number())));
      this.toaster.show({
        text: this.transloco.translate(
          answer.queued ? 'aiAssessment.request.queued' : 'aiAssessment.request.waiting',
        ),
      });
    } catch (error) {
      this.problem.set(problemMessageKey(error, REQUEST_PROBLEMS));
    } finally {
      this.requesting.set(false);
    }
  }

  protected async review(change: ReviewChange): Promise<void> {
    const org = this.orgId();
    if (!org || this.busy()) return;
    const { finding, decision, verdict, level, reason } = change;
    this.busy.set(true);
    this.problem.set(null);
    try {
      await firstValueFrom(
        this.api.reviewFinding(org, finding.id, {
          decision,
          expectedVerdict: finding.verdict,
          expectedSource: finding.verdictSource,
          verdict,
          level,
          reason,
        }),
      );
      for (const r of this.reviews()) r.done();
      this.detail.reload();
      const key =
        decision === 'correct'
          ? 'actionDetail.toast.corrected'
          : finding.verdict === 'not_affected'
            ? 'actionDetail.toast.confirmedClosed'
            : 'actionDetail.toast.confirmed';
      this.toaster.show({ text: this.transloco.translate(key, { entry: finding.entry.key }) });
    } catch (error) {
      this.problem.set(problemMessageKey(error, REVIEW_PROBLEMS));
      if (isStaleReview(problemOf(error)?.code)) this.detail.reload();
    } finally {
      this.busy.set(false);
    }
  }

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
