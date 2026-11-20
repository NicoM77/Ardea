import { Component, computed, inject, input, signal, viewChild } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { LucideChevronRight } from '@lucide/angular';
import { firstValueFrom } from 'rxjs';
import {
  UiBackLink,
  UiButton,
  UiCard,
  UiLoader,
  UiNotice,
  UiPill,
  UiPriority,
  UiStatus,
  UiToaster,
} from 'ui';
import {
  ActionsService,
  getActionResource,
  listMembersResource,
  type ActionDetailView,
} from '../../core/api/client';
import { problemMessageKey, problemOf } from '../../core/api/problem';
import { Dates } from '../../core/format/dates';
import { reloadOnLiveChange } from '../../core/live/live-events';
import { injectOrganization } from '../organizations/current-organization';
import { roleRank } from '../organizations/org-format';
import {
  actionName,
  num,
  priorityOf,
  shownStatus,
  statusKindOf,
  type ActionStatus,
} from './action-format';
import { ActionDetails } from './action-details';
import { ActionHistory } from './action-history';
import { ACTION_PROBLEMS, isStale, storedStatus } from './action-status';
import { AiAssessmentCard } from './ai-assessment-card';
import { Answers } from './answers';
import { FindingsList, type FindingChange } from './findings-list';
import { historyLines } from './history-format';
import { isStaleReview, REVIEW_PROBLEMS } from './model-format';
import type { ReviewChange } from './model-review';
import { OutcomeForm, type OtherClosing } from './outcome-form';

interface Confirmation {
  key: string;
  params?: Record<string, string>;
  undo?: () => void;
  problems?: typeof ACTION_PROBLEMS;
}

@Component({
  selector: 'ardea-action-page',
  imports: [
    NgTemplateOutlet,
    RouterLink,
    TranslocoPipe,
    LucideChevronRight,
    UiBackLink,
    UiButton,
    UiCard,
    UiLoader,
    UiNotice,
    UiPill,
    UiPriority,
    UiStatus,
    ActionDetails,
    ActionHistory,
    AiAssessmentCard,
    Answers,
    FindingsList,
    OutcomeForm,
  ],
  template: `
    <ui-back-link [label]="'actions.title' | transloco" link=".." />
    @if (view(); as v) {
      @let a = v.action;
      <header class="head">
        <div>
          <h1>{{ name().key | transloco: name().params }}</h1>
          <p class="meta">
            <ui-priority [level]="priority()" />
            <a class="link" [routerLink]="['../../projects', a.project.id]">{{ a.project.name }}</a>
            <span class="id">#{{ number() }}</span>
            @if (a.exploitation === 'active') {
              <ui-pill tone="alert">{{ 'actions.pill.exploited' | transloco }}</ui-pill>
            }
            @if (a.fixAvailable) {
              <ui-pill tone="done">{{ 'actions.pill.fix' | transloco }}</ui-pill>
            }
          </p>
        </div>
        @if (canEdit()) {
          <div class="controls">
            <ng-container *ngTemplateOutlet="controls" />
          </div>
        }
      </header>

      @if (problem(); as p) {
        <ui-notice class="problem" tone="error">{{ p | transloco }}</ui-notice>
      }

      <div class="grid">
        <div class="stack">
          <section class="lead-card" [attr.aria-label]="'answers.label' | transloco">
            <div class="status-band">
              <ui-status [kind]="kind()" />
              <p role="status">{{ stateLine().key | transloco: stateLine().params }}</p>
            </div>
            @if (closing()) {
              <ardea-outcome-form
                id="close-form"
                class="close-form"
                [busy]="busy()"
                (save)="closeOther($event)"
                (dismiss)="toggleOther(false)"
              />
            }
            @if (v.answers; as answers) {
              <ardea-answers [answers]="answers" [component]="a.component.name" />
            } @else {
              <p class="none">{{ 'answers.none' | transloco }}</p>
            }
          </section>
          <ui-card [heading]="'actionDetail.vulnerabilities' | transloco">
            <span uiCardMeta>{{ 'findings.meta' | transloco: { count: openFindings() } }}</span>
            <ardea-findings-list
              [findings]="v.findings"
              [canEdit]="canEdit()"
              [busy]="busy()"
              [members]="members.value() ?? []"
              [openFirst]="false"
              (outcome)="setFinding($event)"
              (review)="review($event)"
            />
          </ui-card>
          @if (v.answers && v.answers.origin === 'model' && v.answers.model; as model) {
            <ardea-ai-assessment-card [model]="model" />
          }
          <section class="disclose" [class.is-open]="historyOpen()" aria-labelledby="history-title">
            <h2>
              <button
                type="button"
                aria-controls="history-body"
                [attr.aria-expanded]="historyOpen()"
                (click)="historyOpen.set(!historyOpen())"
              >
                <span class="disclose-title" id="history-title">{{
                  'actionDetail.history.title' | transloco
                }}</span>
                <span class="disclose-meta">{{
                  'actionDetail.historyMeta' | transloco: { count: lines().length }
                }}</span>
                <svg lucideChevronRight aria-hidden="true"></svg>
              </button>
            </h2>
            <div class="disclose-body" id="history-body" [hidden]="!historyOpen()">
              <ardea-action-history
                bare
                [lines]="lines()"
                [canEdit]="canEdit()"
                [busy]="busy()"
                (note)="addNote($event)"
              />
            </div>
          </section>
        </div>
        <div class="stack">
          <ardea-action-details [action]="a" />
        </div>
      </div>

      @if (canEdit()) {
        <div class="ui-actionbar actionbar">
          <ng-container *ngTemplateOutlet="controls" />
        </div>
      }

      <ng-template #controls>
        @if (isOpen()) {
          <button
            uiButton
            variant="primary"
            type="button"
            [loading]="busy() && !closing()"
            (click)="remediate()"
          >
            {{ 'actionDetail.resolve' | transloco }}
          </button>
          <button
            uiButton
            type="button"
            aria-controls="close-form"
            [attr.aria-expanded]="closing()"
            [disabled]="busy()"
            (click)="toggleOther(!closing())"
          >
            {{ 'actionDetail.other' | transloco }}
          </button>
        } @else {
          <button uiButton type="button" [loading]="busy()" (click)="reopen()">
            {{ 'actionDetail.reopen' | transloco }}
          </button>
        }
      </ng-template>
    } @else if (detail.error()) {
      <ui-notice tone="error">{{ loadProblem() | transloco }}</ui-notice>
    } @else {
      <div class="wait"><ui-loader size="page" [label]="'actionDetail.loading' | transloco" /></div>
    }
  `,
  styles: `
    .head {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-end;
      justify-content: space-between;
      gap: var(--space-lg) var(--gap);
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
    .controls,
    .actionbar {
      display: flex;
      align-items: center;
      gap: var(--space-sm);
    }
    .problem {
      margin-bottom: var(--space-lg);
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
    .lead-card {
      border: 1px solid var(--border);
      border-radius: var(--radius-card);
      background: var(--surface);
    }
    .status-band {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-sm) var(--space-lg);
      padding: 14px var(--card-pad);
      border-bottom: 1px solid var(--border);
    }
    .status-band ui-status {
      font-weight: 500;
    }
    .status-band p {
      flex: 1;
      min-width: 200px;
      color: var(--muted);
    }
    .close-form {
      display: block;
      padding: var(--space-lg) var(--card-pad);
      border-bottom: 1px solid var(--border);
      background: var(--raised);
    }
    .none {
      padding: var(--card-pad);
      color: var(--muted);
    }
    .disclose {
      border: 1px solid var(--border);
      border-radius: var(--radius-card);
      background: var(--surface);
    }
    .disclose h2 {
      font-size: var(--fs-section);
      line-height: var(--lh-section);
    }
    .disclose h2 button {
      display: flex;
      align-items: center;
      gap: var(--space-md);
      width: 100%;
      min-height: 56px;
      padding: 0 var(--card-pad);
      border: 0;
      border-radius: var(--radius-card);
      background: none;
      color: inherit;
      font: inherit;
      text-align: left;
      cursor: pointer;
      transition: background-color var(--t-hover) ease;
    }
    .disclose h2 button:focus-visible {
      outline: var(--focus-ring);
      outline-offset: var(--focus-offset);
    }
    .disclose-title {
      flex: 1;
      min-width: 0;
    }
    .disclose-meta {
      color: var(--faint);
      font-size: var(--fs-small);
      white-space: nowrap;
    }
    .disclose h2 svg {
      width: 16px;
      height: 16px;
      color: var(--faint);
      transition: transform var(--t-ui) var(--ease-out);
    }
    .disclose.is-open h2 svg {
      transform: rotate(90deg);
    }
    .disclose.is-open h2 button {
      border-radius: var(--radius-card) var(--radius-card) 0 0;
    }
    .disclose-body {
      padding: var(--space-xs) var(--card-pad) var(--card-pad);
    }
    .wait {
      display: grid;
      place-items: center;
      min-height: 320px;
    }
    .actionbar {
      display: none;
    }
    @media (hover: hover) and (pointer: fine) {
      .disclose h2 button:hover {
        background: var(--raised);
      }
    }
    @media (prefers-reduced-motion: reduce) {
      .disclose h2 svg {
        transition: none;
      }
    }
    @media (max-width: 1100px) {
      .grid {
        grid-template-columns: minmax(0, 1fr);
      }
    }
    @media (max-width: 640px) {
      .head {
        margin-bottom: var(--space-lg);
      }
      .controls {
        display: none;
      }
      .status-band p {
        flex-basis: 100%;
        min-width: 0;
      }
      .actionbar {
        position: fixed;
        left: 0;
        right: 0;
        bottom: 0;
        z-index: 6;
        display: flex;
        padding: var(--space-sm) var(--space-lg) calc(var(--space-sm) + env(safe-area-inset-bottom));
        border-top: 1px solid var(--border);
        background: var(--surface);
      }
      .actionbar > * {
        flex: 1;
        min-height: 44px;
      }
      :host {
        display: block;
        padding-bottom: 72px;
      }
    }
  `,
})
export class ActionPage {
  readonly number = input.required<string>();

  private readonly api = inject(ActionsService);
  private readonly toaster = inject(UiToaster);
  private readonly transloco = inject(TranslocoService);
  private readonly dates = inject(Dates);
  private readonly organization = injectOrganization().organization;
  private readonly orgId = computed(() => this.organization()?.id);
  private readonly findingsList = viewChild(FindingsList);
  private readonly history = viewChild(ActionHistory);

  protected readonly detail = getActionResource(
    this.orgId,
    computed(() => num(this.number()) || undefined),
  );
  protected readonly members = listMembersResource(this.orgId);

  protected readonly view = computed(() =>
    this.detail.hasValue() ? (this.detail.value() ?? null) : null,
  );
  private readonly action = computed(() => this.view()?.action ?? null);
  protected readonly canEdit = computed(
    () => roleRank(this.organization()?.role) >= roleRank('member'),
  );

  protected readonly name = computed(() => {
    const a = this.action();
    return a ? actionName(a) : { key: 'actionDetail.title', params: {} };
  });
  protected readonly priority = computed(() => priorityOf(this.action()?.levelName));
  protected readonly openFindings = computed(() => num(this.action()?.openFindings));
  protected readonly isOpen = computed(() => (this.action()?.status ?? 'open') !== 'closed');
  protected readonly kind = computed(() => {
    const a = this.action();
    return a ? statusKindOf(a.status, a.closeReason) : 'open';
  });
  protected readonly stateLine = computed(() => {
    const a = this.action();
    if (!a) return { key: 'actionDetail.state.open', params: { date: '' } };
    if (a.status !== 'closed')
      return { key: 'actionDetail.state.open', params: { date: this.day(a.openedAt) } };
    const outcome = shownStatus(a);
    const byPerson =
      a.closeReason === 'remediated' ||
      a.closeReason === 'risk_accepted' ||
      a.closeReason === 'not_affected';
    return {
      key: 'actionDetail.state.done',
      params: {
        date: this.day(a.closedAt ?? a.openedAt),
        status: byPerson || outcome !== 'remediated' ? outcome : 'system',
      },
    };
  });
  protected readonly historyOpen = signal(false);
  protected readonly closing = signal(false);
  protected readonly lines = computed(() =>
    historyLines(
      this.view()?.events ?? [],
      this.members.value() ?? [],
      this.view()?.findings ?? [],
    ),
  );

  protected readonly busy = signal(false);
  protected readonly problem = signal<string | null>(null);
  protected readonly loadProblem = computed(() =>
    problemMessageKey(this.detail.error(), {
      statuses: { 404: 'actionDetail.errors.gone' },
      fallback: 'actionDetail.loadFailed',
    }),
  );

  constructor() {
    reloadOnLiveChange(this.detail);
  }

  protected remediate(): void {
    this.closing.set(false);
    this.setStatus('remediated', null, 'actionDetail.toast.remediated');
  }

  protected toggleOther(open: boolean): void {
    this.closing.set(open);
    if (!open) {
      setTimeout(() => {
        const buttons = Array.from(
          document.querySelectorAll<HTMLElement>('ardea-action-page [aria-controls="close-form"]'),
        );
        buttons.find((b) => b.offsetParent !== null)?.focus();
      });
    }
  }

  protected closeOther(closing: OtherClosing): void {
    this.setStatus(closing.outcome, closing.note, 'actionDetail.toast.closed');
  }

  protected reopen(): void {
    const a = this.action();
    if (!a) return;
    const before = shownStatus(a);
    void this.run(() => this.status('open', 'closed', null), {
      key: 'actionDetail.toast.reopened',
      undo: () =>
        void this.run(() => this.status(before, 'open', null), {
          key: 'actionDetail.toast.undone',
        }),
    });
  }

  private setStatus(to: ActionStatus, note: string | null, confirmation: string): void {
    const a = this.action();
    if (!a) return;
    void this.run(() => this.status(to, a.status, note), {
      key: confirmation,
      params: { status: to },
      undo: () =>
        void this.run(() => this.status('open', storedStatus(to), null), {
          key: 'actionDetail.toast.undone',
        }),
    });
  }

  protected addNote(text: string): void {
    const org = this.orgId();
    if (!org) return;
    void this.run(
      async () => {
        await firstValueFrom(this.api.addActionNote(org, num(this.number()), { text }));
        this.history()?.clear();
        this.detail.reload();
        return null;
      },
      { key: 'actionDetail.toast.note' },
    );
  }

  protected setFinding(change: FindingChange): void {
    const org = this.orgId();
    if (!org) return;
    const { finding, status, note } = change;
    void this.run(
      async () => {
        await firstValueFrom(
          this.api.setFindingStatus(org, finding.id, {
            status,
            expectedStatus: finding.status,
            note,
          }),
        );
        this.findingsList()?.done();
        this.detail.reload();
        return null;
      },
      { key: 'actionDetail.toast.finding', params: { entry: finding.entry.key, status } },
    );
  }

  protected review(change: ReviewChange): void {
    const org = this.orgId();
    if (!org) return;
    const { finding, decision, verdict, level, reason } = change;
    const confirmation =
      decision === 'correct'
        ? 'actionDetail.toast.corrected'
        : finding.verdict === 'not_affected'
          ? 'actionDetail.toast.confirmedClosed'
          : 'actionDetail.toast.confirmed';
    void this.run(
      async () => {
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
        this.findingsList()?.done();
        this.detail.reload();
        return null;
      },
      { key: confirmation, params: { entry: finding.entry.key }, problems: REVIEW_PROBLEMS },
    );
  }

  private status(status: ActionStatus, expectedStatus: string, note: string | null) {
    return firstValueFrom(
      this.api.setActionStatus(this.orgId()!, num(this.number()), { status, expectedStatus, note }),
    );
  }

  private day(value: string): string {
    return this.dates.date(value);
  }

  private async run(
    work: () => Promise<ActionDetailView | null>,
    confirmation: Confirmation,
  ): Promise<void> {
    if (this.busy() || !this.orgId()) return;
    this.busy.set(true);
    this.problem.set(null);
    try {
      const view = await work();
      if (view) this.detail.value.set(view);
      this.closing.set(false);
      this.toaster.show({
        text: this.transloco.translate(confirmation.key, confirmation.params ?? {}),
        undo: confirmation.undo,
      });
    } catch (error) {
      this.problem.set(problemMessageKey(error, confirmation.problems ?? ACTION_PROBLEMS));
      const code = problemOf(error)?.code;
      if (isStale(code) || isStaleReview(code)) {
        this.closing.set(false);
        this.detail.reload();
      }
    } finally {
      this.busy.set(false);
    }
  }
}
