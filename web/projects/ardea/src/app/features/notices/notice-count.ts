import { computed, Injectable, signal } from '@angular/core';
import { listNoticesResource, type NoticeView } from '../../core/api/client';
import { reloadOnLiveChange } from '../../core/live/live-events';
import { freshNotices, noticeOrder } from './notice-format';

/** As many as the API gives on one page; a stack of ten entries raises at most 25 a week (ADR 044). */
const PAGE = 100;

/**
 * The new notices of the organization in view (master plan 17.7): the count beside «Notices» in the
 * sidebar and the overview's «n new notices» with the first three. Notices a stack entry brought in
 * quietly (backfill, ADR 047 decision 5) wait under «Earlier» and are not counted. Read again whenever
 * the organization's live stream says something changed; the Notices page sets it after an answer.
 */
@Injectable({ providedIn: 'root' })
export class NoticeCount {
  private readonly organization = signal<string | undefined>(undefined);
  private readonly read = listNoticesResource(
    this.organization,
    signal({ state: 'new', limit: PAGE }),
  );

  readonly ready = computed(() => this.read.hasValue());
  /** The new notices read, in the order of the inbox. */
  readonly fresh = computed<NoticeView[]>(() =>
    this.read.hasValue() ? freshNotices(this.read.value()?.items ?? []).sort(noticeOrder) : [],
  );
  readonly count = computed(() => this.fresh().length);
  /** More open notices than one page holds: the count is a lower bound. */
  readonly more = computed(() => this.read.hasValue() && !!this.read.value()?.nextCursor);

  constructor() {
    reloadOnLiveChange(this.read);
  }

  /** The organization whose notices are counted. */
  watch(organizationId: string | undefined): void {
    this.organization.set(organizationId);
  }

  reload(): void {
    this.read.reload();
  }
}
