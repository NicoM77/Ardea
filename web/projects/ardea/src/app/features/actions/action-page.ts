import { Component, computed, inject, input, signal, viewChild } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { LucideChevronLeft } from '@lucide/angular';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiCard, UiInput, UiLoader, UiNotice, UiPill, UiPriority, UiToaster } from 'ui';
import {
  ActionsService,
  getActionResource,
  listMembersResource,
  type ActionDetailView,
} from '../../core/api/client';
import { problemMessageKey, problemOf } from '../../core/api/problem';
import { reloadOnLiveChange } from '../../core/live/live-events';
import { injectOrganization } from '../organizations/current-organization';
import { roleRank } from '../organizations/org-format';
import {
  actionName,
  isOutcome,
  num,
  priorityOf,
  shownStatus,
  type ActionStatus,
  type Outcome,
} from './action-format';
import { ActionDetails } from './action-details';
import { ActionHistory } from './action-history';
import {
  ACTION_PROBLEMS,
  isStale,
  mainMove,
  STATUS_LABELS,
  statusOptions,
  storedStatus,
} from './action-status';
import { AiAssessmentCard } from './ai-assessment-card';
import { Answers } from './answers';
import { FindingsList, type FindingChange } from './findings-list';
import { historyLines } from './history-format';
import { isStaleReview, REVIEW_PROBLEMS } from './model-format';
import type { ReviewChange } from './model-review';

interface Confirmation {
  key: string;
  params?: Record<string, string>;
  undo?: () => void;
  /** The sentences for a refusal; the action's by default. */
  problems?: typeof ACTION_PROBLEMS;
}

/**
 * One action (task 3.10, master plan 11.4, mockup remediation.html): the three answers first, then
 * its findings, the details with status and the closing's reason, and the history with notes. A
 * reminder is open or done, without owner or deadline (master plan 17.5); the main move marks it
 * remediated. Every change is confirmed by a toast with Undo; a change someone else made meanwhile
 * (409) is said in a sentence and the page shows the current state. On phones the main move and the
 * status sit in a bar at the bottom. With the model's assessment (phase 4, mockup
 * remediation.html?ai) the answers carry its texts, each finding its verdict to confirm or
 * correct, and a card its preconditions, reasoning, sources and budget.
 */
@Component({
  selector: 'ardea-action-page',
  imports: [
    NgTemplateOutlet,
    RouterLink,
    TranslocoPipe,
    LucideChevronLeft,
    UiButton,
    UiCard,
    UiInput,
    UiLoader,
    UiNotice,
    UiPill,
    UiPriority,
    ActionDetails,
    ActionHistory,
    AiAssessmentCard,
    Answers,
    FindingsList,
  ],
  template: `
    <a class="crumb" routerLink="..">
      <svg lucideChevronLeft aria-hidden="true"></svg>{{ 'actions.title' | transloco }}
    </a>
    @if (view(); as v) {
      @let a = v.action;
      <header class="head">
        <div>
          <h1>{{ name().key | transloco: name().params }}</h1>
          <p class="meta">
            <ui-priority [level]="priority()" />
            <span>{{ a.project.name }}</span>
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
            <ng-container *ngTemplateOutlet="controls; context: { id: 'action-status' }" />
          </div>
        }
      </header>

      @if (problem(); as p) {
        <ui-notice class="problem" tone="error">{{ p | transloco }}</ui-notice>
      }

      <div class="grid">
        <div class="stack">
          <section class="answers-card" [attr.aria-label]="'answers.label' | transloco">
            @if (v.answers; as answers) {
              <ardea-answers [answers]="answers" [component]="a.component.name" />
            } @else {
              <p class="none">{{ 'answers.none' | transloco }}</p>
            }
          </section>
          <ui-card [heading]="'findings.title' | transloco">
            <span uiCardMeta>{{ 'findings.meta' | transloco: { count: openFindings() } }}</span>
            <ardea-findings-list
              [findings]="v.findings"
              [canEdit]="canEdit()"
              [busy]="busy()"
              [members]="members.value() ?? []"
              (outcome)="setFinding($event)"
              (review)="review($event)"
            />
          </ui-card>
          @if (v.answers && v.answers.origin === 'model' && v.answers.model; as model) {
            <ardea-ai-assessment-card [model]="model" />
          }
        </div>
        <div class="stack">
          <ardea-action-details
            [action]="a"
            [busy]="busy()"
            [pending]="pending()"
            (reason)="close($event)"
            (cancelReason)="pending.set(null)"
          />
          <ardea-action-history
            [lines]="lines()"
            [canEdit]="canEdit()"
            [busy]="busy()"
            (note)="addNote($event)"
          />
        </div>
      </div>

      @if (canEdit()) {
        <div class="ui-actionbar actionbar">
          <ng-container *ngTemplateOutlet="controls; context: { id: 'action-status-bar' }" />
        </div>
      }

      <ng-template #controls let-id="id">
        @switch (move()) {
          @case ('resolve') {
            <button
              uiButton
              variant="primary"
              type="button"
              [loading]="busy()"
              (click)="choose('remediated')"
            >
              {{ 'actionDetail.resolve' | transloco }}
            </button>
          }
          @default {
            <button uiButton type="button" [loading]="busy()" (click)="choose('open')">
              {{ 'actionDetail.reopen' | transloco }}
            </button>
          }
        }
        <label class="sr-only" [for]="id">{{ 'actionDetail.details.status' | transloco }}</label>
        <select uiInput [id]="id" [disabled]="busy()" (change)="chooseFrom($event)">
          @for (option of options(); track option) {
            <option [value]="option" [selected]="option === selected()">
              {{ statusLabels[option] | transloco }}
            </option>
          }
        </select>
      </ng-template>
    } @else if (detail.error()) {
      <ui-notice tone="error">{{ loadProblem() | transloco }}</ui-notice>
    } @else {
      <div class="wait"><ui-loader size="page" [label]="'actionDetail.loading' | transloco" /></div>
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
      border-radius: var(--radius-control);
      color: var(--muted);
    }
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
    .controls select,
    .actionbar select {
      width: auto;
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
    .answers-card {
      border: 1px solid var(--border);
      border-radius: var(--radius-card);
      background: var(--surface);
    }
    .none {
      padding: var(--card-pad);
      color: var(--muted);
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
      .crumb:hover {
        background: var(--raised);
        color: var(--text);
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
      .actionbar > :first-child {
        flex: 1;
      }
      :host {
        display: block;
        padding-bottom: 72px;
      }
    }
  `,
})
export class ActionPage {
  /** From the route. */
  readonly number = input.required<string>();

  private readonly api = inject(ActionsService);
  private readonly toaster = inject(UiToaster);
  private readonly transloco = inject(TranslocoService);
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
    return a ? actionName(a) : { key: 'actions.title', params: {} };
  });
  protected readonly priority = computed(() => priorityOf(this.action()?.levelName));
  protected readonly openFindings = computed(() => num(this.action()?.openFindings));
  protected readonly move = computed(() => mainMove(this.action()?.status ?? 'open'));
  protected readonly options = computed(() => {
    const a = this.action();
    return a ? statusOptions(a) : [];
  });
  /** The outcome waiting for its reason, shown in the status control meanwhile. */
  protected readonly pending = signal<Outcome | null>(null);
  protected readonly selected = computed(() => {
    const a = this.action();
    return this.pending() ?? (a ? shownStatus(a) : 'open');
  });
  protected readonly statusLabels = STATUS_LABELS;
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

  protected chooseFrom(event: Event): void {
    const select = event.target as HTMLSelectElement;
    const value = select.value as ActionStatus;
    // The control shows the stored status until the change went through.
    select.value = this.selected();
    this.choose(value);
  }

  /** A status from the control: open applies at once, an outcome asks for its reason. */
  protected choose(to: ActionStatus): void {
    const a = this.action();
    if (!a || to === this.selected()) return;
    if (isOutcome(to)) {
      if (a.status !== 'closed') this.pending.set(to);
      return;
    }
    this.pending.set(null);
    this.setStatus(to, null);
  }

  protected close(note: string): void {
    const outcome = this.pending();
    if (outcome) this.setStatus(outcome, note);
  }

  private setStatus(to: ActionStatus, note: string | null): void {
    const a = this.action();
    if (!a) return;
    const before = shownStatus(a);
    const expected = a.status;
    const reopening = expected === 'closed';
    const confirmation: Confirmation = reopening
      ? { key: 'actionDetail.toast.reopened' }
      : { key: 'actionDetail.toast.closed', params: { status: to } };
    void this.run(() => this.status(to, expected, note), {
      ...confirmation,
      undo: () =>
        void this.run(() => this.status(before, storedStatus(to), null), {
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
        // The action may have closed or changed level with it.
        this.detail.reload();
        return null;
      },
      { key: 'actionDetail.toast.finding', params: { entry: finding.entry.key, status } },
    );
  }

  /** A person confirms the model's estimate or corrects the assessment of a finding (task 4.7). */
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
        // The finding was assessed again; the action may have changed level or closed with it.
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

  /** Runs one change: shows what came back, confirms it, or says why it did not work. */
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
      this.pending.set(null);
      this.toaster.show({
        text: this.transloco.translate(confirmation.key, confirmation.params ?? {}),
        undo: confirmation.undo,
      });
    } catch (error) {
      this.problem.set(problemMessageKey(error, confirmation.problems ?? ACTION_PROBLEMS));
      // Someone else changed it: show the state as it is now, and let the person decide again.
      const code = problemOf(error)?.code;
      if (isStale(code) || isStaleReview(code)) {
        this.pending.set(null);
        this.detail.reload();
      }
    } finally {
      this.busy.set(false);
    }
  }
}
