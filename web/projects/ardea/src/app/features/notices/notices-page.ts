import {
  afterNextRender,
  Component,
  computed,
  ElementRef,
  inject,
  Injector,
  input,
  linkedSignal,
  signal,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { LucideChevronRight, LucideCircleCheck } from '@lucide/angular';
import { firstValueFrom } from 'rxjs';
import {
  UiButton,
  UiCard,
  UiEmptyState,
  UiInput,
  UiKbd,
  UiLoader,
  UiNotice,
  UiStatus,
  UiToaster,
} from 'ui';
import {
  getStackResource,
  listActionsResource,
  listMembersResource,
  listNoticesResource,
  listProjectsResource,
  NoticesService,
  type AnswerNoticeRequest,
  type ListPageOfNoticeView,
  type NoticeView,
} from '../../core/api/client';
import { problemMessageKey, problemOf } from '../../core/api/problem';
import { reloadOnLiveChange } from '../../core/live/live-events';
import { num } from '../actions/action-format';
import { injectOrganization } from '../organizations/current-organization';
import { roleRank } from '../organizations/org-format';
import { NoticeCard } from './notice-card';
import { NoticeCount } from './notice-count';
import type { FollowUpAnswer } from './notice-follow-up';
import {
  answeredGroups,
  cardMove,
  freshNotices,
  isEarlier,
  latestById,
  noticeOrder,
  type AnsweredGroup,
} from './notice-format';

/** As many as the API gives on one page. */
const PAGE = 100;

type View = 'new' | 'earlier' | 'answered';

/** Why an answer or taking it back did not go through, in the reader's words (ADR 047 decision 9). */
const NOTICE_PROBLEMS = {
  codes: {
    status_conflict: 'notices.errors.conflict',
    notice_answered: 'notices.errors.conflict',
    notice_without_component: 'notices.errors.withoutComponent',
    project_not_found: 'notices.errors.projectGone',
    project_archived: 'notices.errors.projectArchived',
    invalid_answer: 'notices.errors.invalid',
    invalid_reopen: 'notices.errors.invalid',
    notice_not_found: 'notices.errors.gone',
  },
  statuses: { 403: 'notices.errors.forbidden', 404: 'notices.errors.gone' },
  fallback: 'notices.errors.failed',
};

/** A refusal on one notice: the sentence, and the one under the version when that was the cause. */
interface Refusal {
  id: string;
  key: string | null;
  version: string | null;
  project: string | null;
}

/** One list of notices read page by page; a further page is appended, a notice that moved appears once. */
function pages(
  shown: readonly NoticeView[],
  page: readonly NoticeView[],
  isFirstPage: boolean,
): NoticeView[] {
  if (isFirstPage) return [...page];
  const known = new Set(shown.map((n) => n.id));
  return [...shown, ...page.filter((n) => !known.has(n.id))];
}

/**
 * «Notices» (master plan 17.3, 17.7; ADR 047; mockup notices.html): the inbox «could affect you» of the
 * organization's stack. One card per notice, exploited first, then by the source's CVSS score, then
 * newest (E23); each with the reason, the three answers and the two answers of equal weight. «Affects
 * us» opens a short follow-up (project and version, both optional) and makes a normal reminder;
 * «Doesn't affect us» takes effect at once. Both confirm with Undo, which takes the answer back
 * (ADR 047 decision 8), and the focus moves on to the next card. Notices that a stack entry added later
 * brought in quietly wait under «Earlier». «Answered» groups the answers by stack entry and answer.
 *
 * Keyboard: ↓ or J and ↑ or K move between cards, 1 answers «Affects us», 2 «Doesn't affect us», Esc
 * closes the follow-up. Digits rather than the mockup's A and D, because letters belong to one language.
 */
@Component({
  selector: 'ardea-notices-page',
  imports: [
    NgTemplateOutlet,
    RouterLink,
    TranslocoPipe,
    LucideChevronRight,
    LucideCircleCheck,
    UiButton,
    UiCard,
    UiEmptyState,
    UiInput,
    UiKbd,
    UiLoader,
    UiNotice,
    UiStatus,
    NoticeCard,
  ],
  host: { '(keydown)': 'onKeydown($event)' },
  template: `
    <header class="head">
      <h1>{{ 'notices.title' | transloco }}</h1>
      <!-- The count line stays in the page and only changes its words, so it is read out as notices are answered -->
      <p class="summary" role="status">
        @if (loaded()) {
          <strong>{{ summary().lead.key | transloco: summary().lead.params }}</strong>
          {{ summary().rest.key | transloco: summary().rest.params }}
        }
      </p>
    </header>

    <div class="grid">
      <div class="main">
        <div class="bar">
          <div class="segments" role="group" [attr.aria-label]="'notices.show' | transloco">
            <button type="button" [attr.aria-pressed]="view() === 'new'" (click)="show('new')">
              {{ 'notices.view.new' | transloco }}
              <span class="count">{{ freshCount() }}</span>
            </button>
            @if (earlier().length || view() === 'earlier') {
              <button
                type="button"
                [attr.aria-pressed]="view() === 'earlier'"
                (click)="show('earlier')"
              >
                {{ 'notices.view.earlier' | transloco }}
                <span class="count">{{ earlierCount() }}</span>
              </button>
            }
            <button
              type="button"
              [attr.aria-pressed]="view() === 'answered'"
              (click)="show('answered')"
            >
              {{ 'notices.view.answered' | transloco }}
              <span class="count">{{ answeredCount() }}</span>
            </button>
          </div>
          <label class="filter">
            <span class="sr-only">{{ 'notices.filter.label' | transloco }}</span>
            <select uiInput (change)="setEntry(entrySelect.value)" #entrySelect>
              <option value="" [selected]="!entryId()">
                {{ 'notices.filter.all' | transloco }}
              </option>
              @for (e of entries(); track e.id) {
                <option [value]="e.id" [selected]="e.id === entryId()">{{ e.label }}</option>
              }
            </select>
          </label>
        </div>

        @if (!loaded() && loadProblem()) {
          <ui-notice tone="error">{{ loadProblem()! | transloco }}</ui-notice>
          <button uiButton type="button" class="gap" (click)="reload()">
            {{ 'notices.tryAgain' | transloco }}
          </button>
        } @else if (!loaded()) {
          <div class="wait"><ui-loader size="page" [label]="'notices.loading' | transloco" /></div>
        } @else if (view() !== 'answered') {
          @if (view() === 'earlier') {
            <p class="quiet">{{ 'notices.earlier.text' | transloco }}</p>
          }
          @if (shown().length) {
            @if (canAnswer()) {
              <p class="keys">
                <ui-kbd>↑</ui-kbd><ui-kbd>↓</ui-kbd> {{ 'notices.keys.move' | transloco }}
                <span aria-hidden="true">·</span> <ui-kbd>1</ui-kbd>
                {{ 'notices.keys.affects' | transloco }} <span aria-hidden="true">·</span>
                <ui-kbd>2</ui-kbd> {{ 'notices.keys.notAffected' | transloco }}
                <span aria-hidden="true">·</span> <ui-kbd>Esc</ui-kbd>
                {{ 'notices.keys.cancel' | transloco }}
              </p>
            }
          }
          @if (shown().length) {
            <h2 class="sr-only">{{ listLabel() | transloco }}</h2>
            <ol class="cards" [attr.aria-label]="listLabel() | transloco">
              @for (n of shown(); track n.id) {
                <li>
                  <ng-container *ngTemplateOutlet="card; context: { $implicit: n }"></ng-container>
                </li>
              }
            </ol>
          } @else if (view() === 'earlier') {
            <p class="quiet">{{ 'notices.earlier.none' | transloco }}</p>
          } @else if (entryId()) {
            <p class="quiet">
              {{ 'notices.calm.filtered' | transloco: { entry: entryLabel() } }}
            </p>
          } @else {
            <ui-empty-state
              class="calm"
              id="notices-calm"
              tabindex="-1"
              [heading]="'notices.calm.heading' | transloco"
            >
              <svg uiEmptyIcon lucideCircleCheck aria-hidden="true"></svg>
              {{ 'notices.calm.text' | transloco: { answered: answeredCount() } }}
              <button uiEmptyActions uiButton type="button" (click)="show('answered')">
                {{ 'notices.calm.answered' | transloco }}
              </button>
            </ui-empty-state>
          }
          @if (view() === 'new' ? newMore() : earlierMore()) {
            <div class="more">
              <button
                uiButton
                type="button"
                [loading]="newRead.isLoading() || earlierRead.isLoading()"
                (click)="loadMore(view())"
              >
                {{ 'notices.more' | transloco }}
              </button>
            </div>
          }
        } @else {
          <ui-card [heading]="'notices.answered.title' | transloco" headingId="notices-answered">
            <span uiCardMeta>{{
              'notices.answered.meta' | transloco: { count: answeredCount() }
            }}</span>
            @for (g of groups(); track g.key) {
              <div class="group">
                <div class="group-main">
                  <b>{{
                    'notices.answered.entries'
                      | transloco: { product: g.product, count: g.notices.length }
                  }}</b>
                  <small>
                    <ui-status
                      [kind]="g.answer === 'affects_us' ? 'open' : 'notAffected'"
                      [word]="g.answer === 'affects_us' ? 'notices.affects' : 'notices.notAffected'"
                    />
                    @if (g.answer === 'affects_us') {
                      {{
                        (g.reminderNumber
                          ? 'notices.answered.affects'
                          : 'notices.answered.affectsNoReminder'
                        )
                          | transloco
                            : {
                                number: g.reminderNumber ?? 0,
                                who: person(g.decidedBy),
                                date: day(g.decidedAt),
                              }
                      }}
                    } @else {
                      {{
                        'notices.answered.notAffected'
                          | transloco: { who: person(g.decidedBy), date: day(g.decidedAt) }
                      }}
                    }
                  </small>
                  <small class="keys-list">
                    @for (n of g.notices; track n.id) {
                      <span class="id">{{ n.entry.key }}</span>
                    }
                  </small>
                </div>
                @if (g.answer === 'affects_us' && g.reminderNumber) {
                  <a uiButton variant="ghost" [routerLink]="['../reminders', g.reminderNumber]">
                    {{ 'notices.answered.openReminder' | transloco }}
                  </a>
                } @else if (g.answer === 'not_affected' && canAnswer()) {
                  <button
                    uiButton
                    variant="ghost"
                    type="button"
                    [loading]="busyId() === g.key"
                    (click)="changeAnswer(g)"
                  >
                    {{ 'notices.answered.change' | transloco }}
                  </button>
                }
              </div>
            } @empty {
              <p class="quiet">
                @if (entryId()) {
                  {{ 'notices.answered.noneFor' | transloco: { entry: entryLabel() } }}
                } @else {
                  {{ 'notices.answered.none' | transloco }}
                }
              </p>
            }
            @if (answeredMore()) {
              <div class="more">
                <button
                  uiButton
                  type="button"
                  [loading]="answeredRead.isLoading()"
                  (click)="loadMore('answered')"
                >
                  {{ 'notices.more' | transloco }}
                </button>
              </div>
            }
          </ui-card>
        }
      </div>

      <div class="side">
        <ui-card [heading]="'notices.side.title' | transloco">
          <p class="figure">{{ total() }}</p>
          <p class="figure-note">{{ 'notices.side.note' | transloco }}</p>
          <div class="blocks" role="img" [attr.aria-label]="blocksLabel()">
            @for (b of blocks(); track $index) {
              <i [class]="'is-' + b"></i>
            }
          </div>
          <div class="legend">
            <div>
              <span><i class="is-new"></i>{{ 'notices.view.new' | transloco }}</span
              ><b>{{ freshCount() }}</b>
            </div>
            @if (earlier().length) {
              <div>
                <span><i class="is-earlier"></i>{{ 'notices.earlier.title' | transloco }}</span
                ><b>{{ earlier().length }}</b>
              </div>
            }
            <div>
              <span><i class="is-affects"></i>{{ 'notices.affects' | transloco }}</span
              ><b>{{ affectsCount() }}</b>
            </div>
            <div>
              <span><i class="is-not"></i>{{ 'notices.notAffected' | transloco }}</span
              ><b>{{ notAffectedCount() }}</b>
            </div>
          </div>
        </ui-card>
        <ui-card [heading]="'notices.side.stackTitle' | transloco">
          <a uiCardMeta class="card-link" routerLink="../stack"
            >{{ 'nav.stack' | transloco }}<svg lucideChevronRight aria-hidden="true"></svg
          ></a>
          <p class="figure-note">{{ 'notices.side.stackText' | transloco }}</p>
        </ui-card>
      </div>
    </div>

    <ng-template #card let-n>
      <ardea-notice-card
        [notice]="n"
        [canAnswer]="canAnswer()"
        [following]="following() === n.id"
        [busy]="busyId() === n.id"
        [problem]="refusalOf(n.id)?.key ?? null"
        [versionError]="refusalOf(n.id)?.version ?? null"
        [projectError]="refusalOf(n.id)?.project ?? null"
        [projects]="projects()"
        [reminders]="reminders()"
        [decider]="person(n.decidedBy)"
        (affects)="openFollowUp(n)"
        (notAffected)="notAffected(n)"
        (follow)="affects(n, $event)"
        (dismiss)="closeFollowUp(n)"
      />
    </ng-template>
  `,
  styles: `
    .head {
      margin-bottom: var(--space-xl);
    }
    .summary {
      min-height: var(--lh-body);
      margin-top: var(--space-xs);
      color: var(--muted);
    }
    .summary strong {
      color: var(--text);
      font-weight: 500;
    }
    .grid {
      display: grid;
      grid-template-columns: minmax(0, 8fr) minmax(0, 4fr);
      gap: var(--gap);
      align-items: start;
    }
    .main,
    .side {
      display: grid;
      gap: var(--gap);
      min-width: 0;
    }
    .main {
      gap: var(--space-md);
    }
    .bar {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-md);
    }
    .segments {
      display: inline-flex;
      gap: 2px;
      padding: var(--space-xs);
      border-radius: var(--radius-card);
      background: var(--raised);
    }
    .segments button {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      gap: 6px;
      height: 32px;
      padding: 0 var(--space-md);
      border: 0;
      border-radius: var(--radius-control);
      background: none;
      color: var(--muted);
      cursor: pointer;
    }
    .segments button[aria-pressed='true'] {
      background: var(--surface);
      box-shadow: inset 0 0 0 1px var(--border);
      color: var(--text);
      font-weight: 500;
    }
    .segments button:focus-visible {
      outline: var(--focus-ring);
      outline-offset: var(--focus-offset);
    }
    .count {
      color: var(--faint);
      font-weight: 400;
    }
    .filter select {
      width: auto;
      min-width: 200px;
    }
    .keys {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-xs);
      color: var(--faint);
      font-size: var(--fs-small);
      line-height: 20px;
    }
    .cards {
      display: grid;
      gap: var(--space-md);
    }
    .earlier {
      display: grid;
      gap: var(--space-sm);
      margin-top: var(--space-lg);
    }
    .earlier h2 {
      font-size: var(--fs-section);
      line-height: var(--lh-section);
    }
    .quiet {
      color: var(--muted);
    }
    .wait {
      display: grid;
      place-items: center;
      min-height: 240px;
    }
    .gap {
      justify-self: start;
    }
    .more {
      display: flex;
      justify-content: center;
      margin-top: var(--space-sm);
    }
    .calm:focus {
      outline: none;
    }
    .calm:focus-visible {
      outline: var(--focus-ring);
      outline-offset: var(--focus-offset);
    }
    .group {
      display: flex;
      align-items: center;
      gap: var(--space-md);
      min-height: 56px;
      padding: var(--space-sm) 0;
    }
    .group + .group {
      border-top: 1px solid var(--rule);
    }
    .group-main {
      display: grid;
      flex: 1;
      gap: 2px;
      min-width: 0;
    }
    .group-main b {
      font-weight: 500;
      overflow-wrap: anywhere;
    }
    .group-main small {
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
      overflow-wrap: anywhere;
    }
    .keys-list {
      display: flex;
      flex-wrap: wrap;
      gap: 2px var(--space-sm);
    }
    .group > a,
    .group > button {
      flex: none;
    }
    .figure {
      font-size: var(--fs-kpi);
      line-height: var(--lh-kpi);
      font-weight: 500;
    }
    .figure-note {
      margin-top: var(--space-xs);
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .blocks {
      display: flex;
      flex-wrap: wrap;
      gap: 3px;
      margin: var(--space-md) 0;
    }
    .blocks:empty {
      display: none;
    }
    .blocks i,
    .legend i {
      display: inline-block;
      width: 14px;
      height: 14px;
      border-radius: var(--radius-bar);
      background: var(--dot-faint);
    }
    .legend i {
      width: 10px;
      height: 10px;
      border-radius: var(--radius-meter);
    }
    .blocks i.is-new,
    .legend i.is-new {
      background: var(--accent);
    }
    .blocks i.is-earlier,
    .legend i.is-earlier {
      background: var(--accent-soft);
      box-shadow: inset 0 0 0 1px var(--accent);
    }
    .blocks i.is-affects,
    .legend i.is-affects {
      background: var(--text);
    }
    .blocks i.is-not,
    .legend i.is-not {
      background: var(--dot);
    }
    .legend > div {
      display: flex;
      align-items: center;
      justify-content: space-between;
      min-height: 28px;
    }
    .legend span {
      display: inline-flex;
      align-items: center;
      gap: var(--space-sm);
      color: var(--muted);
    }
    .legend b {
      font-weight: 500;
    }
    .card-link {
      display: inline-flex;
      align-items: center;
      gap: 2px;
      color: var(--muted);
      font-size: var(--fs-sub);
    }
    @media (hover: hover) and (pointer: fine) {
      .card-link:hover {
        color: var(--text);
      }
    }
    @media (hover: none), (pointer: coarse) {
      .keys {
        display: none;
      }
    }
    @media (max-width: 1100px) {
      .grid {
        grid-template-columns: minmax(0, 1fr);
      }
    }
    @media (max-width: 640px) {
      .keys {
        display: none;
      }
      .segments,
      .filter,
      .filter select {
        flex: 1 1 100%;
        width: 100%;
      }
      .segments button {
        flex: 1;
        height: 36px;
      }
      .filter select {
        min-width: 0;
        height: 40px;
      }
      .group {
        flex-wrap: wrap;
      }
      .group > a,
      .group > button {
        margin-left: auto;
      }
    }
  `,
})
export class NoticesPage {
  /** From the address: `view=earlier` or `view=answered`, and `entry=<component id>`. */
  readonly view = input<View, string | null | undefined>('new', {
    transform: (v) => (v === 'answered' || v === 'earlier' ? v : 'new'),
  });
  readonly entry = input<string | null>(null);

  private readonly api = inject(NoticesService);
  private readonly router = inject(Router);
  private readonly toaster = inject(UiToaster);
  private readonly transloco = inject(TranslocoService);
  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly counter = inject(NoticeCount);
  private readonly organization = injectOrganization().organization;
  private readonly orgId = computed(() => this.organization()?.id);

  protected readonly canAnswer = computed(
    () => roleRank(this.organization()?.role) >= roleRank('member'),
  );
  protected readonly entryId = computed(() => this.entry() || null);

  /* ---------- Reading: new and answered, page by page ---------- */
  private readonly newCursor = linkedSignal<string | null, string | undefined>({
    source: this.entryId,
    computation: () => undefined,
  });
  protected readonly newRead = listNoticesResource(
    this.orgId,
    computed(() => ({
      state: 'new',
      component: this.entryId() ?? undefined,
      limit: PAGE,
      cursor: this.newCursor(),
    })),
  );
  private readonly answeredCursor = linkedSignal<string | null, string | undefined>({
    source: this.entryId,
    computation: () => undefined,
  });
  protected readonly answeredRead = listNoticesResource(
    this.orgId,
    computed(() => ({
      state: 'answered',
      component: this.entryId() ?? undefined,
      limit: PAGE,
      cursor: this.answeredCursor(),
    })),
  );
  /** Raised quietly when a stack entry was added or changed (ADR 047): not new, under «Earlier». */
  private readonly earlierCursor = linkedSignal<string | null, string | undefined>({
    source: this.entryId,
    computation: () => undefined,
  });
  protected readonly earlierRead = listNoticesResource(
    this.orgId,
    computed(() => ({
      state: 'earlier',
      component: this.entryId() ?? undefined,
      limit: PAGE,
      cursor: this.earlierCursor(),
    })),
  );
  private readonly newRows = this.rowsOf(this.newRead, this.newCursor);
  private readonly earlierRows = this.rowsOf(this.earlierRead, this.earlierCursor);
  private readonly answeredRows = this.rowsOf(this.answeredRead, this.answeredCursor);
  /** The answers given and taken back on this page, newer than the pages read before them. */
  private readonly changed = signal<NoticeView[]>([]);

  protected readonly loaded = computed(
    () => this.newRead.hasValue() && this.earlierRead.hasValue() && this.answeredRead.hasValue(),
  );
  protected readonly loadProblem = computed(() => {
    const error = this.newRead.error() ?? this.earlierRead.error() ?? this.answeredRead.error();
    return error ? problemMessageKey(error, { fallback: 'notices.loadFailed' }) : null;
  });

  private readonly all = computed(() => {
    const entry = this.entryId();
    return latestById(
      this.newRows(),
      this.earlierRows(),
      this.answeredRows(),
      this.changed(),
    ).filter((n) => !entry || n.components.some((c) => c.id === entry));
  });
  protected readonly fresh = computed(() => freshNotices(this.all()).sort(noticeOrder));
  protected readonly earlier = computed(() => this.all().filter(isEarlier).sort(noticeOrder));
  private readonly answered = computed(() =>
    this.all().filter((n) => n.state === 'affects_us' || n.state === 'not_affected'),
  );
  protected readonly groups = computed(() => answeredGroups(this.answered()));
  protected readonly newMore = computed(() => this.nextOf(this.newRead) !== null);
  protected readonly earlierMore = computed(() => this.nextOf(this.earlierRead) !== null);
  protected readonly answeredMore = computed(() => this.nextOf(this.answeredRead) !== null);
  /** The cards of the view: new, or earlier. */
  protected readonly shown = computed(() =>
    this.view() === 'earlier' ? this.earlier() : this.fresh(),
  );
  protected readonly listLabel = computed(() =>
    this.view() === 'earlier' ? 'notices.list.earlier' : 'notices.list.new',
  );

  protected readonly freshCount = computed(
    () => `${this.fresh().length}${this.newMore() ? '+' : ''}`,
  );
  protected readonly earlierCount = computed(
    () => `${this.earlier().length}${this.earlierMore() ? '+' : ''}`,
  );
  protected readonly answeredCount = computed(
    () => `${this.answered().length}${this.answeredMore() ? '+' : ''}`,
  );
  protected readonly affectsCount = computed(
    () => this.answered().filter((n) => n.state === 'affects_us').length,
  );
  protected readonly notAffectedCount = computed(
    () => this.answered().filter((n) => n.state === 'not_affected').length,
  );
  protected readonly total = computed(
    () => this.fresh().length + this.earlier().length + this.answered().length,
  );

  /** The count line: «3 new notices might affect you. 5 answered.» */
  protected readonly summary = computed(() => {
    const count = this.fresh().length;
    const answered = this.answered().length;
    return count
      ? {
          lead: { key: 'notices.summary.new', params: { count } },
          rest: { key: 'notices.summary.newRest', params: { answered } },
        }
      : {
          lead: { key: 'notices.summary.none', params: {} },
          rest: { key: 'notices.summary.noneRest', params: { answered } },
        };
  });

  /** One block per notice, new first, then earlier, «affects us» and «doesn't affect us». */
  protected readonly blocks = computed(() => [
    ...this.fresh().map(() => 'new'),
    ...this.earlier().map(() => 'earlier'),
    ...Array.from({ length: this.affectsCount() }, () => 'affects'),
    ...Array.from({ length: this.notAffectedCount() }, () => 'not'),
  ]);
  protected readonly blocksLabel = computed(() =>
    this.transloco.translate('notices.side.blocks', {
      total: this.total(),
      fresh: this.fresh().length,
      earlier: this.earlier().length,
      affects: this.affectsCount(),
      not: this.notAffectedCount(),
    }),
  );

  /* ---------- What the follow-up and the filter need ---------- */
  private readonly stack = getStackResource(this.orgId);
  protected readonly entries = computed(() => {
    const components = this.stack.hasValue() ? (this.stack.value()?.components ?? []) : [];
    return components
      .map((c) => ({ id: c.id, label: c.version ? `${c.name} ${c.version}` : c.name }))
      .sort((a, b) => a.label.localeCompare(b.label));
  });
  protected readonly entryLabel = computed(
    () => this.entries().find((e) => e.id === this.entryId())?.label ?? '',
  );
  private readonly projectsRead = listProjectsResource(this.orgId);
  protected readonly projects = computed(() =>
    this.projectsRead.hasValue()
      ? (this.projectsRead.value() ?? [])
          .filter((p) => !p.archived)
          .map((p) => ({ id: p.id, name: p.name }))
      : [],
  );
  private readonly openReminders = listActionsResource(
    this.orgId,
    signal({ status: 'active', limit: 100 }),
  );
  protected readonly reminders = computed(() =>
    this.openReminders.hasValue() ? (this.openReminders.value()?.items ?? []) : [],
  );
  private readonly members = listMembersResource(this.orgId);

  /* ---------- Answering ---------- */
  protected readonly following = signal<string | null>(null);
  protected readonly busyId = signal<string | null>(null);
  private readonly refusal = signal<Refusal | null>(null);

  constructor() {
    reloadOnLiveChange(
      {
        // A change can move notices between pages: start again from the first.
        reload: () => this.reload(),
      },
      this.openReminders,
    );
  }

  protected refusalOf(id: string): Refusal | null {
    const r = this.refusal();
    return r && r.id === id ? r : null;
  }

  protected person(id: string | null): string | null {
    if (!id) return null;
    const members = this.members.hasValue() ? (this.members.value() ?? []) : [];
    return members.find((m) => m.userId === id)?.displayName ?? null;
  }

  protected day(value: string | null): string {
    if (!value) return '';
    return new Intl.DateTimeFormat(this.transloco.getActiveLang(), { dateStyle: 'medium' }).format(
      new Date(value),
    );
  }

  protected reload(): boolean {
    if (this.newCursor()) this.newCursor.set(undefined);
    else this.newRead.reload();
    if (this.earlierCursor()) this.earlierCursor.set(undefined);
    else this.earlierRead.reload();
    if (this.answeredCursor()) this.answeredCursor.set(undefined);
    else this.answeredRead.reload();
    return true;
  }

  protected loadMore(list: View): void {
    if (list === 'new') {
      const next = this.nextOf(this.newRead);
      if (next) this.newCursor.set(next);
    } else if (list === 'earlier') {
      const next = this.nextOf(this.earlierRead);
      if (next) this.earlierCursor.set(next);
    } else {
      const next = this.nextOf(this.answeredRead);
      if (next) this.answeredCursor.set(next);
    }
  }

  protected show(view: View): void {
    this.following.set(null);
    this.refusal.set(null);
    void this.router.navigate([], {
      queryParams: { view: view === 'new' ? null : view },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  protected setEntry(id: string): void {
    this.following.set(null);
    void this.router.navigate([], {
      queryParams: { entry: id || null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  protected openFollowUp(n: NoticeView): void {
    if (!this.canAnswer() || this.busyId()) return;
    this.refusal.set(null);
    this.following.set(n.id);
  }

  protected closeFollowUp(n: NoticeView): void {
    this.following.set(null);
    this.refusal.set(null);
    this.focusCard(n.id);
  }

  protected notAffected(n: NoticeView): void {
    void this.answer(n, { answer: 'not_affected', expectedState: 'open' }, () =>
      n.reappearedAt ? 'notices.toast.notAffectedFinal' : 'notices.toast.notAffected',
    );
  }

  protected affects(n: NoticeView, follow: FollowUpAnswer): void {
    const request: AnswerNoticeRequest = {
      answer: 'affects_us',
      expectedState: 'open',
      projectId: follow.projectId,
      version: follow.version,
    };
    void this.answer(n, request, (view) =>
      view.reminderNumber === null
        ? 'notices.toast.affectsOutOfRange'
        : this.reminders().some((r) => num(r.number) === num(view.reminderNumber))
          ? 'notices.toast.affectsJoined'
          : 'notices.toast.affectsCreated',
    );
  }

  /** «Change answer» in the answered view: every notice of the group back to New, with Undo. */
  protected async changeAnswer(group: AnsweredGroup): Promise<void> {
    const org = this.orgId();
    if (!org || this.busyId()) return;
    this.busyId.set(group.key);
    const back: NoticeView[] = [];
    try {
      for (const n of group.notices) {
        back.push(
          await firstValueFrom(this.api.reopenNotice(org, n.id, { expectedState: n.state })),
        );
        this.remember(back[back.length - 1]);
      }
      this.counter.reload();
      this.toaster.show({
        text: this.transloco.translate('notices.toast.movedBack', { count: back.length }),
        undo: () => void this.answerAgain(org, back),
      });
    } catch (error) {
      this.failed(error, null);
    } finally {
      this.busyId.set(null);
    }
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (this.view() === 'answered' || !this.canAnswer() || this.following()) return;
    const target = event.target as HTMLElement | null;
    if (!target || target.closest('input, textarea, select, [contenteditable="true"]')) return;
    const card = target.closest<HTMLElement>('[data-notice]');
    const move = card ? cardMove(event) : null;
    if (!card || !move) return;
    event.preventDefault();
    const cards = this.cardElements();
    const index = cards.indexOf(card);
    const notice = this.all().find((n) => n.id === card.dataset['notice']);
    switch (move) {
      case 'next':
        cards[index + 1]?.focus();
        break;
      case 'previous':
        cards[index - 1]?.focus();
        break;
      case 'affects':
        if (notice) this.openFollowUp(notice);
        break;
      case 'notAffected':
        if (notice) this.notAffected(notice);
        break;
    }
  }

  /** Answers one notice; on success the next card takes the focus and the toast offers Undo. */
  private async answer(
    n: NoticeView,
    request: AnswerNoticeRequest,
    confirmation: (view: NoticeView) => string,
  ): Promise<void> {
    const org = this.orgId();
    if (!org || this.busyId()) return;
    this.busyId.set(n.id);
    this.refusal.set(null);
    const index = this.cardElements().findIndex((c) => c.dataset['notice'] === n.id);
    try {
      const view = await firstValueFrom(this.api.answerNotice(org, n.id, request));
      this.remember(view);
      this.following.set(null);
      this.counter.reload();
      this.openReminders.reload();
      this.focusAfter(index);
      this.toaster.show({
        text: this.transloco.translate(confirmation(view), {
          number: num(view.reminderNumber),
          version: request.version ?? '',
        }),
        undo: () => void this.takeBack(org, view),
      });
    } catch (error) {
      this.failed(error, n.id);
    } finally {
      this.busyId.set(null);
    }
  }

  /** Undo of an answer: the plain reopen of ADR 047 decision 8, against the answer just given. */
  private async takeBack(org: string, view: NoticeView): Promise<void> {
    try {
      const back = await firstValueFrom(
        this.api.reopenNotice(org, view.id, { expectedState: view.state }),
      );
      this.remember(back);
      this.counter.reload();
      this.openReminders.reload();
      this.focusCard(back.id);
      this.toaster.show({ text: this.transloco.translate('notices.toast.undone') });
    } catch (error) {
      this.failed(error, null);
    }
  }

  /** Undo of «Change answer»: «Doesn't affect us» again for each notice moved back. */
  private async answerAgain(org: string, notices: readonly NoticeView[]): Promise<void> {
    try {
      for (const n of notices) {
        this.remember(
          await firstValueFrom(
            this.api.answerNotice(org, n.id, { answer: 'not_affected', expectedState: 'open' }),
          ),
        );
      }
      this.counter.reload();
      this.toaster.show({ text: this.transloco.translate('notices.toast.undone') });
    } catch (error) {
      this.failed(error, null);
    }
  }

  private remember(view: NoticeView): void {
    this.changed.update((list) => [...list.filter((n) => n.id !== view.id), view]);
  }

  /** Says why a change did not go through: on the card when it has one, else in a toast. */
  private failed(error: unknown, id: string | null): void {
    const problem = problemOf(error);
    const version = problem?.errors?.['version'] ? 'notices.errors.version' : null;
    const project = problem?.errors?.['projectId'] ? 'notices.errors.projectNeeded' : null;
    const key = version || project ? null : problemMessageKey(error, NOTICE_PROBLEMS);
    if (id) this.refusal.set({ id, key, version, project });
    else this.toaster.show({ text: this.transloco.translate(key ?? 'notices.errors.failed') });
    // Someone else answered or took it back meanwhile: read the notices as they are now.
    if (problem?.status === 409 || problem?.status === 404) this.reload();
  }

  private cardElements(): HTMLElement[] {
    return Array.from(this.host.querySelectorAll<HTMLElement>('[data-notice]'));
  }

  private focusCard(id: string): void {
    afterNextRender(
      () => this.host.querySelector<HTMLElement>(`[data-notice="${CSS.escape(id)}"]`)?.focus(),
      { injector: this.injector },
    );
  }

  /** After an answer the card at the same place (the next one) takes the focus, else the quiet state. */
  private focusAfter(index: number): void {
    afterNextRender(
      () => {
        const cards = this.cardElements();
        const next = cards[Math.min(Math.max(index, 0), cards.length - 1)];
        if (next) next.focus();
        else this.host.querySelector<HTMLElement>('#notices-calm')?.focus();
      },
      { injector: this.injector },
    );
  }

  private nextOf(read: { hasValue(): boolean; value(): ListPageOfNoticeView | undefined }) {
    return read.hasValue() ? (read.value()?.nextCursor ?? null) : null;
  }

  private rowsOf(
    read: { hasValue(): boolean; value(): ListPageOfNoticeView | undefined },
    cursor: () => string | undefined,
  ) {
    return linkedSignal<
      { entry: string | null; cursor: string | undefined; items: NoticeView[] | undefined },
      NoticeView[]
    >({
      source: () => ({
        entry: this.entryId(),
        cursor: cursor(),
        items: read.hasValue() ? read.value()?.items : undefined,
      }),
      computation: (source, previous) => {
        const shown = previous && previous.source.entry === source.entry ? previous.value : [];
        return source.items ? pages(shown, source.items, !source.cursor) : shown;
      },
    });
  }
}
