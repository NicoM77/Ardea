import { computed, Injectable, signal } from '@angular/core';
import { listNoticesResource, type NoticeView } from '../../core/api/client';
import { reloadOnLiveChange } from '../../core/live/live-events';
import { freshNotices, noticeOrder } from './notice-format';

const PAGE = 100;

@Injectable({ providedIn: 'root' })
export class NoticeCount {
  private readonly organization = signal<string | undefined>(undefined);
  private readonly read = listNoticesResource(
    this.organization,
    signal({ state: 'new', limit: PAGE }),
  );

  readonly ready = computed(() => this.read.hasValue());
  readonly failed = computed(() => !!this.read.error());
  readonly fresh = computed<NoticeView[]>(() =>
    this.read.hasValue() ? freshNotices(this.read.value()?.items ?? []).sort(noticeOrder) : [],
  );
  readonly count = computed(() => this.fresh().length);
  readonly more = computed(() => this.read.hasValue() && !!this.read.value()?.nextCursor);

  constructor() {
    reloadOnLiveChange(this.read);
  }

  watch(organizationId: string | undefined): void {
    this.organization.set(organizationId);
  }

  reload(): void {
    this.read.reload();
  }
}
