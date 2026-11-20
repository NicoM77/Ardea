import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { LucideChevronRight, LucideLayoutGrid } from '@lucide/angular';
import { UiButton, UiCard, UiEmptyState, UiLoader, UiNotice, UiPriority } from 'ui';
import {
  getOverviewResource,
  getStatusWarningsResource,
  getWizardStateResource,
  listActionsResource,
  listEntriesResource,
  listSourcesResource,
  type ActionSummary,
  type NoticeView,
} from '../../core/api/client';
import { problemMessageKey } from '../../core/api/problem';
import { reloadOnLiveChange } from '../../core/live/live-events';
import { actionName, num, priorityOf } from '../actions/action-format';
import { Answers } from '../actions/answers';
import { NoticeCount } from '../notices/notice-count';
import { countText, cvssText, reasonComponent } from '../notices/notice-format';
import { injectOrganization } from '../organizations/current-organization';
import { roleRank } from '../organizations/org-format';
import { ChangesCard, ProjectsCard, SourcesCard, WarningsCard } from './overview-cards';
import { openByPriority, overviewFinding, projectBlocks } from './overview-format';
import { PriorityRing } from './priority-ring';

const WEEK = 7 * 86_400_000;

/**
 * «Overview» (task 3.8, master plan 11.4 and 17.7, mockup overview.html): the day at a glance. One
 * sentence on what needs attention, the open reminders with the most urgent one's three answers, the
 * new notices leading the side column above the split by priority, projects, what changed since yesterday, the warnings of national centres without an
 * identifier and the state of the sources. Reminders have no deadlines (master plan 17.5), so
 * nothing counts down. On a quiet day the same cards stay in place and say that nothing is urgent,
 * so the page never changes shape.
 */
@Component({
  selector: 'ardea-overview-page',
  imports: [
    RouterLink,
    TranslocoPipe,
    LucideChevronRight,
    LucideLayoutGrid,
    UiButton,
    UiCard,
    UiEmptyState,
    UiLoader,
    UiNotice,
    UiPriority,
    Answers,
    ChangesCard,
    PriorityRing,
    ProjectsCard,
    SourcesCard,
    WarningsCard,
  ],
  templateUrl: './overview-page.html',
  styleUrl: './overview-page.css',
})
export class OverviewPage {
  private readonly organization = injectOrganization().organization;
  private readonly orgId = computed(() => this.organization()?.id);

  protected readonly overview = getOverviewResource(this.orgId);
  private readonly open = listActionsResource(this.orgId, signal({ status: 'active', limit: 100 }));
  private readonly warningsRead = listEntriesResource(
    signal({ kind: 'warning', changedSince: new Date(Date.now() - WEEK).toISOString(), limit: 5 }),
  );
  private readonly sourcesRead = listSourcesResource();
  private readonly statusRead = getStatusWarningsResource();
  private readonly setup = getWizardStateResource(this.orgId);

  protected readonly data = computed(() =>
    this.overview.hasValue() ? (this.overview.value() ?? null) : null,
  );
  private readonly openRows = computed<ActionSummary[]>(() =>
    this.open.hasValue() ? (this.open.value()?.items ?? []) : [],
  );
  protected readonly problem = computed(() =>
    problemMessageKey(this.overview.error(), { fallback: 'overview.loadFailed' }),
  );

  protected readonly finding = computed(() => {
    const data = this.data();
    return data ? overviewFinding(data, this.openRows()) : null;
  });
  protected readonly counts = computed(() => {
    const data = this.data();
    return data ? openByPriority(data) : { critical: 0, high: 0, medium: 0, low: 0 };
  });
  protected readonly total = computed(() =>
    Object.values(this.counts()).reduce((n, c) => n + c, 0),
  );
  protected readonly urgent = computed(() => this.data()?.urgent ?? []);
  protected readonly projects = computed(() => {
    const data = this.data();
    return data ? projectBlocks(data, this.openRows()) : [];
  });
  protected readonly warnings = computed(() =>
    this.warningsRead.hasValue() ? (this.warningsRead.value()?.items ?? []) : [],
  );
  protected readonly sources = computed(() =>
    this.sourcesRead.hasValue() ? (this.sourcesRead.value() ?? []) : [],
  );
  protected readonly statusWarnings = computed(() =>
    this.statusRead.hasValue() ? (this.statusRead.value() ?? []) : [],
  );
  /** A member of an organization whose setup is unfinished is offered to continue it (11.6). */
  protected readonly offerSetup = computed(
    () =>
      roleRank(this.organization()?.role) >= roleRank('member') &&
      this.setup.hasValue() &&
      !this.setup.value()?.completedAt,
  );

  /** The new notices (master plan 17.7): their count leads the side column, the first three below. */
  private readonly notices = inject(NoticeCount);
  protected readonly noticeCount = this.notices.count;
  protected readonly noticesKnown = this.notices.ready;
  protected readonly noticesFailed = this.notices.failed;
  /** «3», «99+», or «100+» when a further page was not read. */
  protected readonly noticeText = computed(() =>
    countText(this.notices.count(), this.notices.more()),
  );
  protected readonly peek = computed(() => this.notices.fresh().slice(0, 3));
  protected peekProduct(n: NoticeView): string {
    return reasonComponent(n)?.name ?? '?';
  }
  protected readonly peekScore = cvssText;

  /** A priority pointed at in the ring's legend stands out in the ring. */
  protected readonly focus = signal<string | null>(null);

  constructor() {
    reloadOnLiveChange(this.overview, this.open);
  }

  protected readonly name = actionName;
  protected readonly priority = priorityOf;
  protected readonly number = num;
}
