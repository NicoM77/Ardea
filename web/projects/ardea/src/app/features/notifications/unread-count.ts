import { computed, Injectable, linkedSignal, signal } from '@angular/core';
import { countUnreadNotificationsResource } from '../../core/api/client';
import { reloadOnLiveChange } from '../../core/live/live-events';
import { num } from '../actions/action-format';

@Injectable({ providedIn: 'root' })
export class UnreadCount {
  private readonly organization = signal<string | undefined>(undefined);
  private readonly read = countUnreadNotificationsResource(this.organization);

  readonly count = linkedSignal(() =>
    this.read.hasValue() ? num(this.read.value()?.unread ?? 0) : 0,
  );
  readonly ready = computed(() => this.read.hasValue());

  constructor() {
    reloadOnLiveChange(this.read);
  }

  watch(organizationId: string | undefined): void {
    this.organization.set(organizationId);
  }

  set(count: number): void {
    this.count.set(count);
  }
}
