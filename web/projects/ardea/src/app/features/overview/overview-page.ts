import { Component, computed, signal } from '@angular/core';
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
} from '../../core/api/client';
import { problemMessageKey } from '../../core/api/problem';
import { reloadOnLiveChange } from '../../core/live/live-events';
import { actionName, num, priorityOf, timeLeft } from '../actions/action-format';
import { Answers } from '../actions/answers';
import { Duration, injectNow } from '../actions/duration';
import { injectOrganization } from '../organizations/current-organization';
import { roleRank } from '../organizations/org-format';
import { DeadlineRunway } from './deadline-runway';
import { ChangesCard, ProjectsCard, SourcesCard, WarningsCard } from './overview-cards';
import {
  byDeadline,
  openByPriority,
  overviewFinding,
  projectBlocks,
  runway,
} from './overview-format';
import { PriorityRing } from './priority-ring';

const WEEK = 7 * 86_400_000;

/**
 * «Overview» (task 3.8, master plan 11.4, mockup overview.html): the day at a glance. One sentence
 * on what needs attention, every deadline on one axis, the next one counting down, the split by
 * priority, the most urgent action with its three answers, projects, what changed since yesterday,
 * the warnings of national centres without an identifier and the state of the sources. On a quiet
 * day the same cards stay in place and say that nothing is urgent, so the page never changes shape.
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
    DeadlineRunway,
    Duration,
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
  /** The countdown moves every second; nothing else on the page does. */
  protected readonly now = injectNow(1000);

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
    // The sentence changes with the day, not with the second.
    return data ? overviewFinding(data, this.openRows(), this.minute()) : null;
  });
  private readonly minute = computed(
    () => new Date(Math.floor(this.now().getTime() / 60_000) * 60_000),
    { equal: (a, b) => a.getTime() === b.getTime() },
  );
  protected readonly counts = computed(() => {
    const data = this.data();
    return data ? openByPriority(data) : { critical: 0, high: 0, medium: 0, low: 0 };
  });
  protected readonly total = computed(() =>
    Object.values(this.counts()).reduce((n, c) => n + c, 0),
  );
  protected readonly runway = computed(() => runway(this.openRows(), this.minute()));
  protected readonly next = computed(() => byDeadline(this.openRows())[0] ?? null);
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

  /** A priority pointed at in the ring's legend stands out in the runway. */
  protected readonly focus = signal<string | null>(null);

  constructor() {
    reloadOnLiveChange(this.overview, this.open);
  }

  protected readonly name = actionName;
  protected readonly priority = priorityOf;
  protected readonly number = num;

  protected left(action: ActionSummary): number {
    return timeLeft(action.dueAt!, this.now());
  }
}
