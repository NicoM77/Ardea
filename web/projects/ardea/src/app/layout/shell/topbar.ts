import { CdkMenu, CdkMenuItem, CdkMenuTrigger } from '@angular/cdk/menu';
import { Component, effect, inject, input, output } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import {
  LucideBell,
  LucideBellRing,
  LucideKeyRound,
  LucideLogOut,
  LucideMenu,
  LucideSearch,
} from '@lucide/angular';
import { UiAvatar, UiButton, UiKbd } from 'ui';
import type { SessionOrganization } from '../../core/api/client';
import type { Me } from '../../core/session/session';
import { UnreadCount } from '../../features/notifications/unread-count';

@Component({
  selector: 'ardea-topbar',
  imports: [
    CdkMenu,
    CdkMenuItem,
    CdkMenuTrigger,
    RouterLink,
    RouterLinkActive,
    TranslocoPipe,
    LucideBell,
    LucideBellRing,
    LucideKeyRound,
    LucideLogOut,
    LucideMenu,
    LucideSearch,
    UiAvatar,
    UiButton,
    UiKbd,
  ],
  template: `
    <button
      uiButton
      variant="ghost"
      iconOnly
      type="button"
      class="menu-toggle"
      aria-controls="sidebar"
      [attr.aria-expanded]="menuOpen()"
      [attr.aria-label]="'shell.menu.open' | transloco"
      (click)="openMenu.emit()"
    >
      <svg lucideMenu aria-hidden="true"></svg>
    </button>
    <a routerLink="/" class="mark">
      <img src="brand/ardea-mark.svg" [alt]="'shell.home' | transloco" width="24" height="24" />
    </a>

    <button
      type="button"
      class="search"
      aria-haspopup="dialog"
      (click)="openSearch.emit($event.detail > 0)"
    >
      <svg lucideSearch aria-hidden="true"></svg>
      <span class="search-text">{{ 'search.label' | transloco }}</span>
      <span class="keys" aria-hidden="true">
        <ui-kbd>/</ui-kbd>
        <ui-kbd>{{ 'search.ctrlK' | transloco }}</ui-kbd>
      </span>
    </button>

    @if (organization(); as org) {
      <a
        class="bell"
        [routerLink]="'/o/' + org.slug + '/notifications'"
        routerLinkActive="is-current"
        ariaCurrentWhenActive="page"
        [attr.aria-label]="'shell.bell' | transloco: { count: unread.count() }"
      >
        <svg lucideBell aria-hidden="true"></svg>
        @if (unread.count() > 0) {
          <span class="count" aria-hidden="true">{{
            unread.count() > 99 ? '99+' : unread.count()
          }}</span>
        }
      </a>
    }

    @if (user(); as me) {
      <button
        type="button"
        class="account"
        [cdkMenuTriggerFor]="account"
        [attr.aria-label]="'shell.account.label' | transloco: { name: me.displayName }"
      >
        <ui-avatar [name]="me.displayName" [personId]="me.id" />
      </button>

      <ng-template #account>
        <div class="ui-menu" cdkMenu>
          <div class="ui-menu-head">
            <b>{{ me.displayName }}</b>
            <small>{{ me.email }}</small>
          </div>
          @if (organization(); as org) {
            <a
              class="ui-menu-item"
              cdkMenuItem
              [routerLink]="'/o/' + org.slug + '/settings/notifications'"
            >
              <svg lucideBellRing aria-hidden="true"></svg>
              {{ 'notify.settings.title' | transloco }}
            </a>
          }
          <a class="ui-menu-item" cdkMenuItem routerLink="/account/two-factor">
            <svg lucideKeyRound aria-hidden="true"></svg>
            {{ 'shell.account.twoFactor' | transloco }}
          </a>
          <div class="ui-menu-sep" role="separator"></div>
          <button
            class="ui-menu-item"
            cdkMenuItem
            type="button"
            (cdkMenuItemTriggered)="signOut.emit()"
          >
            <svg lucideLogOut aria-hidden="true"></svg>
            {{ 'shell.account.signOut' | transloco }}
          </button>
        </div>
      </ng-template>
    }
  `,
  styles: `
    :host {
      position: relative;
      display: flex;
      align-items: center;
      justify-content: center;
      gap: var(--space-md);
      height: var(--topbar);
      padding: 0 var(--space-lg) 0 0;
      background: var(--ground);
    }
    .menu-toggle,
    .mark {
      display: none;
    }
    .mark {
      border-radius: var(--radius-key);
    }
    .mark img {
      width: 24px;
      height: 24px;
    }
    .search {
      display: flex;
      align-items: center;
      gap: var(--space-sm);
      width: min(520px, 100%);
      height: 32px;
      padding: 0 var(--space-sm) 0 10px;
      border: 1px solid var(--border);
      border-radius: var(--radius-card);
      background: var(--surface);
      color: var(--faint);
      cursor: pointer;
      transition:
        border-color var(--t-hover) ease,
        transform var(--t-press) var(--ease-out);
    }
    .search:active {
      transform: scale(0.99);
    }
    @media (prefers-reduced-motion: reduce) {
      .search:active {
        transform: none;
      }
    }
    .search:focus-visible,
    .account:focus-visible {
      outline: var(--focus-ring);
      outline-offset: var(--focus-offset);
    }
    .search-text {
      flex: 1;
      min-width: 0;
      overflow: hidden;
      text-align: left;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .keys {
      display: inline-flex;
      gap: var(--space-xs);
    }
    .bell {
      position: absolute;
      right: calc(var(--space-lg) + 40px);
      display: grid;
      place-items: center;
      width: 32px;
      height: 32px;
      border-radius: var(--radius-control);
      color: var(--muted);
    }
    .bell svg {
      width: 18px;
      height: 18px;
    }
    .bell.is-current {
      background: var(--active);
      color: var(--text);
    }
    .bell:focus-visible {
      outline: var(--focus-ring);
      outline-offset: var(--focus-offset);
    }
    .count {
      position: absolute;
      top: 1px;
      right: -2px;
      min-width: 16px;
      height: 16px;
      padding: 0 4px;
      border-radius: var(--radius-pill);
      background: var(--primary);
      color: var(--primary-ink);
      font-size: 11px;
      font-weight: 600;
      line-height: 16px;
      text-align: center;
    }
    .account {
      position: absolute;
      right: var(--space-lg);
      display: grid;
      place-items: center;
      width: 32px;
      height: 32px;
      padding: 0;
      border: 0;
      border-radius: 50%;
      background: none;
      cursor: pointer;
    }
    @media (hover: hover) and (pointer: fine) {
      .search:hover {
        border-color: var(--control);
      }
    }
    @media (max-width: 900px) {
      :host {
        justify-content: flex-start;
        gap: var(--space-sm);
        padding: 0 var(--space-md);
      }
      .menu-toggle {
        display: inline-flex;
      }
      .mark {
        display: block;
      }
      .search {
        flex: 1;
        width: auto;
      }
      .keys {
        display: none;
      }
      .bell {
        position: relative;
        right: auto;
        width: 40px;
        height: 40px;
      }
      .account {
        position: static;
        width: 40px;
        height: 40px;
      }
    }
  `,
})
export class Topbar {
  readonly user = input<Me | null>(null);
  readonly organization = input<SessionOrganization | null>(null);
  protected readonly unread = inject(UnreadCount);

  constructor() {
    effect(() => this.unread.watch(this.organization()?.id));
  }

  readonly menuOpen = input(false);
  readonly openMenu = output();
  readonly openSearch = output<boolean>();
  readonly signOut = output();
}
