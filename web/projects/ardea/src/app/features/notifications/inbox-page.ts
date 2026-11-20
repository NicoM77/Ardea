import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { LucideBell, LucideMegaphone } from '@lucide/angular';
import { UiButton, UiEmptyState, UiLoader, UiNotice, UiPageHeader, UiPriority } from 'ui';
import { firstValueFrom } from 'rxjs';
import {
  listInAppNotificationsResource,
  NotificationsService,
  type InAppNotificationView,
} from '../../core/api/client';
import { problemMessageKey } from '../../core/api/problem';
import { reloadOnLiveChange } from '../../core/live/live-events';
import { num } from '../actions/action-format';
import { injectOrganization } from '../organizations/current-organization';
import { notificationPriority } from './notify-format';
import { NotifyIcon } from './notify-icon';
import { UnreadCount } from './unread-count';

export function dayKey(createdAt: string, now: Date): string {
  const day = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
  const diff = Math.round((day(now) - day(new Date(createdAt))) / 86_400_000);
  return diff <= 0
    ? 'today'
    : diff === 1
      ? 'yesterday'
      : new Date(day(new Date(createdAt))).toISOString();
}

@Component({
  selector: 'ardea-inbox-page',
  imports: [
    NgTemplateOutlet,
    RouterLink,
    TranslocoPipe,
    LucideBell,
    LucideMegaphone,
    NotifyIcon,
    UiButton,
    UiEmptyState,
    UiLoader,
    UiNotice,
    UiPageHeader,
    UiPriority,
  ],
  template: `
    @if (organization(); as org) {
      <ui-page-header
        [heading]="'notify.inbox.title' | transloco"
        [sub]="'notify.inbox.sub' | transloco: { count: unread(), name: org.name }"
      >
        <a
          uiButton
          variant="ghost"
          uiPageActions
          [routerLink]="'/o/' + org.slug + '/settings/notifications'"
        >
          {{ 'notify.inbox.settings' | transloco }}
        </a>
      </ui-page-header>

      <div class="tools">
        <div class="segments" role="group" [attr.aria-label]="'notify.inbox.show' | transloco">
          <button type="button" [attr.aria-pressed]="!onlyUnread()" (click)="onlyUnread.set(false)">
            {{ 'notify.inbox.all' | transloco }}
          </button>
          <button type="button" [attr.aria-pressed]="onlyUnread()" (click)="onlyUnread.set(true)">
            {{ 'notify.inbox.unread' | transloco }} <span class="count">{{ unread() }}</span>
          </button>
        </div>
        @if (unread() > 0) {
          <button uiButton type="button" [loading]="marking()" (click)="markAll()">
            {{ 'notify.inbox.markAll' | transloco }}
          </button>
        }
      </div>
      <div class="live" role="status">
        @if (problem()) {
          <ui-notice tone="error">{{ problem() | transloco }}</ui-notice>
        }
      </div>

      @if (items(); as list) {
        @if (list.length) {
          @for (group of groups(); track group.key) {
            <h2 class="day">{{ group.label }}</h2>
            <ul class="items">
              @for (n of group.items; track n.id) {
                <li>
                  @if (n.link) {
                    <a
                      class="item"
                      [class.unread]="!n.readAt"
                      [routerLink]="n.link"
                      (click)="open(n)"
                    >
                      <ng-container
                        [ngTemplateOutlet]="row"
                        [ngTemplateOutletContext]="{ $implicit: n }"
                      />
                    </a>
                  } @else {
                    <button type="button" class="item" [class.unread]="!n.readAt" (click)="open(n)">
                      <ng-container
                        [ngTemplateOutlet]="row"
                        [ngTemplateOutletContext]="{ $implicit: n }"
                      />
                    </button>
                  }
                </li>
              }
            </ul>
          }
          @if (hasMore()) {
            <div class="more">
              <button
                uiButton
                type="button"
                [loading]="list$.isLoading()"
                (click)="limit.set(limit() + 30)"
              >
                {{ 'notify.inbox.more' | transloco }}
              </button>
            </div>
          }
        } @else {
          <ui-empty-state
            [heading]="(onlyUnread() ? 'notify.inbox.caughtUp' : 'notify.inbox.empty') | transloco"
          >
            <svg uiEmptyIcon lucideBell aria-hidden="true"></svg>
            {{ 'notify.inbox.emptyText' | transloco }}
          </ui-empty-state>
        }
      } @else if (list$.error()) {
        <ui-notice tone="error">{{ 'notify.loadFailed' | transloco }}</ui-notice>
      } @else {
        <ui-loader [label]="'notify.loading' | transloco" />
      }

      <ng-template #row let-n>
        <span class="dot" aria-hidden="true"></span>
        <span class="icon">
          @if (priorityOf(n); as p) {
            <ui-priority [level]="p" symbolOnly />
          } @else if (n.kind === 'warning.published') {
            <svg lucideMegaphone aria-hidden="true"></svg>
          } @else if (n.kind === 'digest') {
            <ardea-notify-icon name="inbox" />
          } @else {
            <svg lucideBell aria-hidden="true"></svg>
          }
        </span>
        <span class="text">
          <b>{{ n.title }}</b>
          <span class="body">{{ n.body }}</span>
        </span>
        <span class="when">
          {{ time(n.createdAt) }}
          @if (!n.readAt) {
            <span class="sr-only">{{ 'notify.inbox.isUnread' | transloco }}</span>
          }
        </span>
      </ng-template>
    }
  `,
  styles: `
    :host {
      display: block;
      max-width: 860px;
      margin: 0 auto;
    }
    .tools {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-md);
      margin-bottom: var(--space-lg);
    }
    .segments {
      display: inline-flex;
      gap: 2px;
      padding: var(--space-xs);
      border-radius: var(--radius-card);
      background: var(--raised);
    }
    .segments button {
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
    .live:empty {
      display: none;
    }
    .day {
      margin: var(--space-xl) 0 var(--space-xs);
      color: var(--faint);
      font-size: var(--fs-small);
      font-weight: 500;
      line-height: var(--lh-small);
    }
    .day:first-of-type {
      margin-top: 0;
    }
    .items {
      display: grid;
    }
    .items li + li {
      border-top: 1px solid var(--rule);
    }
    .item {
      width: calc(100% + 2 * var(--space-sm));
      border: 0;
      background: none;
      font: inherit;
      text-align: left;
      display: grid;
      grid-template-columns: 8px 24px minmax(0, 1fr) auto;
      align-items: start;
      gap: var(--space-xs) var(--space-md);
      padding: var(--space-md) var(--space-sm);
      margin: 0 calc(-1 * var(--space-sm));
      border-radius: var(--radius-control);
      color: var(--text);
      cursor: pointer;
      transition: background-color var(--t-hover) ease;
    }
    .item:focus-visible {
      outline: var(--focus-ring);
      outline-offset: 0;
    }
    .dot {
      width: 8px;
      height: 8px;
      margin-top: 6px;
      border-radius: 50%;
    }
    .unread .dot {
      background: var(--accent);
    }
    .icon {
      display: grid;
      place-items: center;
      height: 20px;
      color: var(--muted);
    }
    .icon svg {
      width: 16px;
      height: 16px;
    }
    .text {
      display: grid;
      gap: 2px;
      min-width: 0;
    }
    .text b {
      font-weight: 400;
      overflow-wrap: anywhere;
    }
    .unread .text b {
      font-weight: 600;
    }
    .body {
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .when {
      color: var(--faint);
      font-size: var(--fs-small);
      white-space: nowrap;
    }
    .more {
      display: flex;
      justify-content: center;
      margin-top: var(--space-lg);
    }
    @media (hover: hover) and (pointer: fine) {
      .item:hover {
        background: var(--raised);
      }
    }
    @media (max-width: 640px) {
      .item {
        grid-template-columns: 8px 24px minmax(0, 1fr);
      }
      .when {
        grid-column: 3;
      }
    }
  `,
})
export class InboxPage {
  private readonly api = inject(NotificationsService);
  private readonly transloco = inject(TranslocoService);
  private readonly unreadCount = inject(UnreadCount);
  protected readonly organization = injectOrganization().organization;
  private readonly orgId = computed(() => this.organization()?.id);

  protected readonly onlyUnread = signal(false);
  protected readonly limit = signal(30);
  protected readonly marking = signal(false);
  protected readonly problem = signal<string | null>(null);

  protected readonly list$ = listInAppNotificationsResource(
    this.orgId,
    computed(() => ({ unread: this.onlyUnread() || undefined, limit: this.limit() })),
  );
  protected readonly items = computed(() =>
    this.list$.hasValue() ? (this.list$.value()?.items ?? null) : null,
  );
  protected readonly unread = computed(() =>
    this.list$.hasValue() ? num(this.list$.value()?.unread ?? 0) : 0,
  );
  protected readonly hasMore = computed(
    () => !!(this.list$.hasValue() && this.list$.value()?.nextCursor) && this.limit() < 100,
  );
  protected readonly groups = computed(() => {
    const now = new Date();
    const groups: { key: string; label: string; items: InAppNotificationView[] }[] = [];
    for (const n of this.items() ?? []) {
      const key = dayKey(n.createdAt, now);
      let group = groups.find((g) => g.key === key);
      if (!group) {
        const label =
          key === 'today' || key === 'yesterday'
            ? this.transloco.translate(
                key === 'today' ? 'notify.inbox.today' : 'notify.inbox.yesterday',
              )
            : new Intl.DateTimeFormat(this.transloco.getActiveLang(), { dateStyle: 'full' }).format(
                new Date(key),
              );
        group = { key, label, items: [] };
        groups.push(group);
      }
      group.items.push(n);
    }
    return groups;
  });

  constructor() {
    reloadOnLiveChange(this.list$);
  }

  protected priorityOf(n: InAppNotificationView) {
    return notificationPriority(n.level);
  }

  protected time(value: string): string {
    return new Intl.DateTimeFormat(this.transloco.getActiveLang(), { timeStyle: 'short' }).format(
      new Date(value),
    );
  }

  protected open(n: InAppNotificationView): void {
    if (!n.readAt) void this.mark([n.id]);
  }

  protected async markAll(): Promise<void> {
    this.marking.set(true);
    await this.mark(null);
    this.marking.set(false);
  }

  private async mark(ids: string[] | null): Promise<void> {
    const org = this.orgId();
    if (!org) return;
    this.problem.set(null);
    try {
      const result = await firstValueFrom(
        this.api.markNotificationsRead(org, ids ? { ids, all: null } : { ids: null, all: true }),
      );
      this.unreadCount.set(num(result.unread));
      this.list$.reload();
    } catch (error) {
      this.problem.set(problemMessageKey(error, { fallback: 'notify.inbox.markFailed' }));
    }
  }
}
