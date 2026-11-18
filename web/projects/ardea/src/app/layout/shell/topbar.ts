import { CdkMenu, CdkMenuItem, CdkMenuTrigger } from '@angular/cdk/menu';
import { Component, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { LucideKeyRound, LucideLogOut, LucideMenu, LucideSearch } from '@lucide/angular';
import { UiAvatar, UiButton, UiKbd } from 'ui';
import type { Me } from '../../core/session/session';

/**
 * The top bar on the ground (48px): the search, which opens with `/` or Ctrl K, and the account
 * menu. Below 900px it also holds the menu button and the heron mark, because the sidebar folds.
 */
@Component({
  selector: 'ardea-topbar',
  imports: [
    CdkMenu,
    CdkMenuItem,
    CdkMenuTrigger,
    RouterLink,
    TranslocoPipe,
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
      <span class="search-text">{{ 'search.trigger' | transloco }}</span>
      <span class="keys" aria-hidden="true">
        <ui-kbd>/</ui-kbd>
        <ui-kbd>{{ 'search.ctrlK' | transloco }}</ui-kbd>
      </span>
    </button>

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
  readonly menuOpen = input(false);
  readonly openMenu = output();
  /** `true` when opened with the pointer: the dialog then scales in. */
  readonly openSearch = output<boolean>();
  readonly signOut = output();
}
