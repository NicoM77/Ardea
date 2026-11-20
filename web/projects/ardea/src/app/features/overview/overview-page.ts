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
  protected readonly offerSetup = computed(
    () =>
      roleRank(this.organization()?.role) >= roleRank('member') &&
      this.setup.hasValue() &&
      !this.setup.value()?.completedAt,
  );

  private readonly notices = inject(NoticeCount);
  protected readonly noticeCount = this.notices.count;
  protected readonly noticesKnown = this.notices.ready;
  protected readonly noticesFailed = this.notices.failed;
  protected readonly noticeText = computed(() =>
    countText(this.notices.count(), this.notices.more()),
  );
  protected readonly peek = computed(() => this.notices.fresh().slice(0, 3));
  protected peekProduct(n: NoticeView): string {
    return reasonComponent(n)?.name ?? '?';
  }
  protected readonly peekScore = cvssText;

  protected readonly focus = signal<string | null>(null);

  constructor() {
    reloadOnLiveChange(this.overview, this.open);
  }

  protected readonly name = actionName;
  protected readonly priority = priorityOf;
  protected readonly number = num;
}
