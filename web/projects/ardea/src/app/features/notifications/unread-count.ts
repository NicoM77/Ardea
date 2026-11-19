import { computed, Injectable, linkedSignal, signal } from '@angular/core';
import { countUnreadNotificationsResource } from '../../core/api/client';
import { reloadOnLiveChange } from '../../core/live/live-events';
import { num } from '../actions/action-format';

/**
 * The unread counter of the bell in the top bar (master plan 8.5, task 5.3): the signed-in member's unread
 * notifications in the organization being viewed, read again whenever the organization's live stream says
 * something changed. The inbox sets it at once after marking.
 */
@Injectable({ providedIn: 'root' })
export class UnreadCount {
  private readonly organization = signal<string | undefined>(undefined);
  private readonly read = countUnreadNotificationsResource(this.organization);

  /** Unread notifications; 0 while unknown. */
  readonly count = linkedSignal(() =>
    this.read.hasValue() ? num(this.read.value()?.unread ?? 0) : 0,
  );
  readonly ready = computed(() => this.read.hasValue());

  constructor() {
    reloadOnLiveChange(this.read);
  }

  /** The organization whose notifications are counted. */
  watch(organizationId: string | undefined): void {
    this.organization.set(organizationId);
  }

  set(count: number): void {
    this.count.set(count);
  }
}
