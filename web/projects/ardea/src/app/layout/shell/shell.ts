import { CdkTrapFocus } from '@angular/cdk/a11y';
import { Dialog } from '@angular/cdk/dialog';
import { BreakpointObserver } from '@angular/cdk/layout';
import { Component, computed, effect, ElementRef, inject, signal, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { LucideKeyRound } from '@lucide/angular';
import { filter, map } from 'rxjs';
import { AuthApi } from '../../core/api/auth-api';
import { Session } from '../../core/session/session';
import { ADMINISTRATION, AREAS } from '../../features/areas/areas';
import { SearchDialog, SearchDialogData } from '../search/search-dialog';
import { opensSearch, SearchItem } from '../search/search';
import { Sidebar } from './sidebar';
import { Topbar } from './topbar';

const SLUG = /^\/o\/([^/?#]+)/;

/**
 * The signed-in frame (master plan 11.4, DESIGN.md Layout): a 228px sidebar and a 48px top bar
 * on the grey-blue ground, the work on a white sheet inset 8px. Below 900px the sidebar becomes
 * a full-screen menu behind a button in the top bar.
 */
@Component({
  selector: 'ardea-shell',
  imports: [CdkTrapFocus, RouterOutlet, TranslocoPipe, Sidebar, Topbar],
  host: {
    '[class.is-narrow]': 'narrow()',
    '(document:keydown)': 'onKeydown($event)',
  },
  template: `
    <a class="skip-link" href="#main" (click)="skipToContent($event)">{{
      'shell.skip' | transloco
    }}</a>

    <ardea-sidebar
      id="sidebar"
      class="sidebar"
      [class.open]="menuOpen()"
      [attr.inert]="narrow() && !menuOpen() ? '' : null"
      [attr.role]="narrow() ? 'dialog' : null"
      [attr.aria-modal]="narrow() && menuOpen() ? 'true' : null"
      [attr.aria-label]="'shell.nav' | transloco"
      [cdkTrapFocus]="narrow() && menuOpen()"
      [user]="session.user()"
      [organization]="organization()"
      [menuMode]="narrow()"
      (closeMenu)="closeMenu()"
      (navigated)="closeMenu(false)"
    />

    <ardea-topbar
      class="topbar"
      [user]="session.user()"
      [menuOpen]="menuOpen()"
      (openMenu)="openMenu()"
      (openSearch)="openSearch($event)"
      (signOut)="signOut()"
    />

    <main class="sheet" id="main" tabindex="-1" #main>
      <div class="content">
        <router-outlet />
      </div>
    </main>
  `,
  styles: `
    :host {
      display: grid;
      grid-template-columns: var(--sidebar-width) minmax(0, 1fr);
      grid-template-rows: var(--topbar) auto;
      min-height: 100vh;
      min-height: 100dvh;
    }
    .sidebar {
      grid-row: 1 / 3;
      position: sticky;
      top: 0;
      height: 100vh;
      height: 100dvh;
    }
    .topbar {
      position: sticky;
      top: 0;
      z-index: 5;
    }
    .sheet {
      min-width: 0;
      margin: 0 var(--space-sm) var(--space-sm) 0;
      padding: var(--pad) var(--pad) var(--space-2xl);
      border: 1px solid var(--border);
      border-radius: var(--radius-sheet);
      background: var(--surface);
      min-height: calc(100vh - var(--topbar) - var(--space-sm));
      min-height: calc(100dvh - var(--topbar) - var(--space-sm));
    }
    .sheet:focus {
      outline: none;
    }
    .content {
      max-width: var(--content-max);
      margin: 0 auto;
      animation: shell-content-in 260ms var(--ease-out) both;
      view-transition-name: content;
    }
    .skip-link {
      position: fixed;
      left: var(--space-md);
      top: -48px;
      z-index: 30;
      padding: var(--space-sm) var(--space-md);
      border-radius: var(--radius-control);
      background: var(--primary);
      color: var(--primary-ink);
      font-weight: 500;
    }
    .skip-link:focus {
      top: var(--space-sm);
    }
    @keyframes shell-content-in {
      from {
        opacity: 0;
        transform: translateY(6px);
      }
    }
    @media (max-width: 900px) {
      :host {
        grid-template-columns: minmax(0, 1fr);
      }
      .sidebar {
        position: fixed;
        inset: 0;
        z-index: 20;
        width: 100%;
        height: 100%;
        padding: var(--space-md) var(--space-lg);
        transform: translateX(-100%);
        visibility: hidden;
        transition:
          transform 320ms cubic-bezier(0.32, 0.72, 0, 1),
          visibility 0s 320ms;
      }
      .sidebar.open {
        transform: none;
        visibility: visible;
        transition-delay: 0s;
      }
      .sheet {
        margin: 0;
        border-width: 1px 0 0;
        border-radius: var(--radius-sheet) var(--radius-sheet) 0 0;
        min-height: calc(100vh - var(--topbar));
        min-height: calc(100dvh - var(--topbar));
      }
    }
    @media (prefers-reduced-motion: reduce) {
      .content {
        animation: shell-content-fade 200ms ease both;
      }
      @keyframes shell-content-fade {
        from {
          opacity: 0;
        }
      }
    }
  `,
})
export class Shell {
  protected readonly session = inject(Session);
  private readonly auth = inject(AuthApi);
  private readonly router = inject(Router);
  private readonly dialog = inject(Dialog);
  private readonly transloco = inject(TranslocoService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly main = viewChild.required<ElementRef<HTMLElement>>('main');

  protected readonly narrow = toSignal(
    inject(BreakpointObserver)
      .observe('(max-width: 900px)')
      .pipe(map((state) => state.matches)),
    { initialValue: false },
  );
  protected readonly menuOpen = signal(false);

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map((event) => event.urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );

  /** The organization in the address, or the first one for pages outside an organization. */
  protected readonly organization = computed(() => {
    const orgs = this.session.user()?.organizations ?? [];
    const slug = SLUG.exec(this.url())?.[1];
    return orgs.find((org) => org.slug === slug) ?? orgs[0] ?? null;
  });

  constructor() {
    // Leaving the phone width with the menu open must not leave it open on the desktop layout.
    effect(() => {
      if (!this.narrow()) this.menuOpen.set(false);
    });
  }

  protected openMenu(): void {
    this.menuOpen.set(true);
    setTimeout(() => this.host.querySelector<HTMLElement>('#sidebar .close')?.focus());
  }

  protected closeMenu(restoreFocus = true): void {
    if (!this.menuOpen()) return;
    this.menuOpen.set(false);
    if (restoreFocus) this.host.querySelector<HTMLElement>('.menu-toggle')?.focus();
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape' && this.menuOpen()) {
      event.preventDefault();
      this.closeMenu();
      return;
    }
    const target = event.target as HTMLElement | null;
    const typing = !!target?.closest('input, textarea, select, [contenteditable="true"]');
    if (opensSearch(event, typing) && this.dialog.openDialogs.length === 0) {
      event.preventDefault();
      this.openSearch(false);
    }
  }

  protected openSearch(byPointer: boolean): void {
    if (this.dialog.openDialogs.length > 0) return;
    this.dialog.open<void, SearchDialogData>(SearchDialog, {
      data: { items: this.searchItems(), byPointer },
      ariaLabel: this.transloco.translate('search.label'),
      backdropClass: 'ui-scrim',
      panelClass: 'ui-dialog-pane',
    });
  }

  protected async signOut(): Promise<void> {
    try {
      await this.auth.logout();
    } finally {
      await this.session.refresh();
      await this.router.navigate(['/sign-in'], { queryParams: { notice: 'signedOut' } });
    }
  }

  protected skipToContent(event: Event): void {
    event.preventDefault();
    this.main().nativeElement.focus();
  }

  private searchItems(): SearchItem[] {
    const t = (key: string) => this.transloco.translate(key);
    const items: SearchItem[] = [];
    const org = this.organization();
    if (org) {
      for (const area of AREAS) {
        items.push({
          label: t(area.label),
          group: org.name,
          url: `/o/${org.slug}/${area.path}`,
          icon: area.icon,
        });
      }
    }
    const account = t('search.groups.account');
    items.push({
      label: t('shell.account.twoFactor'),
      group: account,
      url: '/account/two-factor',
      icon: LucideKeyRound.icon,
      keywords: t('search.keywords.twoFactor'),
    });
    if (this.session.user()?.isInstanceAdmin) {
      items.push({
        label: t(ADMINISTRATION.label),
        group: t('search.groups.instance'),
        url: ADMINISTRATION.path,
        icon: ADMINISTRATION.icon,
      });
    }
    return items;
  }
}
