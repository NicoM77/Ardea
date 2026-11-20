import { CdkTrapFocus } from '@angular/cdk/a11y';
import { Dialog } from '@angular/cdk/dialog';
import { BreakpointObserver } from '@angular/cdk/layout';
import {
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  Injector,
  signal,
  viewChild,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { Router, RouterOutlet } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { LucideBellRing, LucideKeyRound } from '@lucide/angular';
import { UiToastHost } from 'ui';
import { firstValueFrom, map } from 'rxjs';
import { AuthenticationService } from '../../core/api/client';
import { CurrentOrganization } from '../../core/session/current-organization';
import { Session } from '../../core/session/session';
import { ICON_CONFIG } from '../../core/icons';
import { AREA_ICONS } from '../../features/areas/area-icons';
import { ADMINISTRATION, AREAS, areaUrl, isInstanceWide } from '../../features/areas/areas';
import { NoticeCount } from '../../features/notices/notice-count';
import { countText } from '../../features/notices/notice-format';
import { PushDevice } from '../../features/notifications/push-device';
import { SearchDialog, SearchDialogData } from '../search/search-dialog';
import { opensSearch, SearchItem, VulnerabilitySearch } from '../search/search';
import { Sidebar } from './sidebar';
import { Topbar } from './topbar';

@Component({
  selector: 'ardea-shell',
  providers: [ICON_CONFIG],
  imports: [CdkTrapFocus, RouterOutlet, TranslocoPipe, Sidebar, Topbar, UiToastHost],
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
      [attr.aria-label]="narrow() ? ('shell.menu.label' | transloco) : null"
      [cdkTrapFocus]="narrow() && menuOpen()"
      [user]="session.user()"
      [organization]="organization()"
      [noticeCount]="noticeCount()"
      [menuMode]="narrow()"
      (closeMenu)="closeMenu()"
      (navigated)="closeMenu(false)"
    />

    <ardea-topbar
      class="topbar"
      [user]="session.user()"
      [organization]="organization()"
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

    <ui-toast-host />
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
      .sidebar,
      .sidebar.open {
        transition: none;
      }
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
  private readonly auth = inject(AuthenticationService);
  private readonly push = inject(PushDevice);
  private readonly router = inject(Router);
  private readonly dialog = inject(Dialog);
  private readonly injector = inject(Injector);
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

  protected readonly organization = inject(CurrentOrganization).organization;

  private readonly notices = inject(NoticeCount);
  protected readonly noticeCount = computed(() =>
    this.notices.count() > 0 ? countText(this.notices.count(), this.notices.more()) : null,
  );

  constructor() {
    effect(() => this.notices.watch(this.organization()?.id));
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
      data: { items: this.searchItems(), vulnerabilities: this.vulnerabilitySearch(), byPointer },
      ariaLabel: this.transloco.translate('search.label'),
      backdropClass: 'ui-scrim',
      panelClass: 'ui-dialog-pane',
      injector: this.injector,
    });
  }

  protected async signOut(): Promise<void> {
    try {
      await this.push.forget();
      await firstValueFrom(this.auth.logout());
    } finally {
      await this.session.refresh();
      await this.router.navigate(['/sign-in'], { queryParams: { notice: 'signedOut' } });
    }
  }

  protected skipToContent(event: Event): void {
    event.preventDefault();
    this.main().nativeElement.focus();
  }

  private vulnerabilitySearch(): VulnerabilitySearch {
    const t = (key: string, params?: Record<string, string>) =>
      this.transloco.translate(key, params);
    return {
      group: t('nav.vulnerabilities'),
      icon: AREA_ICONS.vulnerabilities,
      open: (key) => t('search.openEntry', { key }),
      search: (query) => t('search.vulnerabilities', { query }),
    };
  }

  private searchItems(): SearchItem[] {
    const t = (key: string) => this.transloco.translate(key);
    const items: SearchItem[] = [];
    const org = this.organization();
    const instance = t('search.groups.instance');
    const ordered = [...AREAS].sort(
      (a, b) => Number(isInstanceWide(a)) - Number(isInstanceWide(b)),
    );
    for (const area of ordered) {
      const url = areaUrl(area, org?.slug);
      if (!url) continue;
      items.push({
        label: t(area.label),
        group: isInstanceWide(area) ? instance : (org?.name ?? instance),
        url,
        icon: AREA_ICONS[area.id],
      });
    }
    const account = t('search.groups.account');
    if (org) {
      items.push({
        label: t('notify.settings.title'),
        group: account,
        url: `/o/${org.slug}/settings/notifications`,
        icon: LucideBellRing.icon,
        keywords: t('search.keywords.notifications'),
      });
    }
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
        icon: AREA_ICONS.administration,
      });
    }
    return items;
  }
}
