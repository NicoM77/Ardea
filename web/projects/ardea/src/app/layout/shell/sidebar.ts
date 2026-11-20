import { booleanAttribute, Component, computed, input, output } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { LucideDynamicIcon, LucideX } from '@lucide/angular';
import { UiButton } from 'ui';
import type { SessionOrganization } from '../../core/api/client';
import type { Me } from '../../core/session/session';
import { AREA_ICONS } from '../../features/areas/area-icons';
import { ADMINISTRATION, Area, AREAS, areaUrl } from '../../features/areas/areas';
import { OrgSwitch } from './org-switch';

interface NavLink {
  area: Area;
  url: string;
}

@Component({
  selector: 'ardea-sidebar',
  imports: [
    RouterLink,
    RouterLinkActive,
    TranslocoPipe,
    LucideDynamicIcon,
    LucideX,
    UiButton,
    OrgSwitch,
  ],
  template: `
    <div class="brand">
      <a routerLink="/" class="logo">
        <img src="brand/ardea-logo.svg" [alt]="'shell.home' | transloco" width="85" height="26" />
      </a>
      <button
        uiButton
        variant="ghost"
        iconOnly
        type="button"
        class="close"
        [attr.aria-label]="'shell.menu.close' | transloco"
        (click)="closeMenu.emit()"
      >
        <svg lucideX aria-hidden="true"></svg>
      </button>
    </div>

    <ardea-org-switch [organizations]="user()?.organizations ?? []" [current]="organization()" />

    <nav class="links" [attr.aria-label]="'shell.nav' | transloco">
      <ul class="nav">
        @for (link of mainLinks(); track link.area.id) {
          <li>
            <a
              [routerLink]="link.url"
              routerLinkActive="is-current"
              ariaCurrentWhenActive="page"
              (click)="navigated.emit()"
            >
              <svg [lucideIcon]="icons[link.area.id]" aria-hidden="true"></svg>
              <span>{{ link.area.label | transloco }}</span>
              @if (link.area.id === 'notices' && noticeCount()) {
                <span class="count" aria-hidden="true">{{ noticeCount() }}</span>
                <span class="sr-only"
                  >&ngsp;{{ 'nav.noticesNew' | transloco: { count: noticeCount() } }}</span
                >
              }
            </a>
          </li>
        }
      </ul>

      <div class="foot">
        <ul class="nav">
          @for (link of footLinks(); track link.area.id) {
            <li>
              <a
                [routerLink]="link.url"
                routerLinkActive="is-current"
                ariaCurrentWhenActive="page"
                (click)="navigated.emit()"
              >
                <svg [lucideIcon]="icons[link.area.id]" aria-hidden="true"></svg>
                <span>{{ link.area.label | transloco }}</span>
              </a>
            </li>
          }
        </ul>
      </div>
    </nav>
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: 2px;
      padding: var(--space-md);
      overflow-y: auto;
      background: var(--ground);
    }
    .brand {
      display: flex;
      align-items: center;
      justify-content: space-between;
      height: 36px;
      padding: 0 var(--space-sm);
      margin-bottom: var(--space-sm);
    }
    .logo {
      border-radius: var(--radius-key);
    }
    .logo img {
      height: 26px;
      width: auto;
    }
    .close {
      display: none;
    }
    :host(.is-menu) .close {
      display: inline-flex;
    }
    .nav {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }
    .nav a {
      display: flex;
      align-items: center;
      gap: 10px;
      height: 32px;
      padding: 0 var(--space-sm);
      border-radius: var(--radius-card);
      color: var(--muted);
      transition:
        background-color var(--t-hover) ease,
        color var(--t-hover) ease,
        transform var(--t-press) var(--ease-out);
    }
    .nav a:active {
      transform: scale(0.98);
    }
    @media (prefers-reduced-motion: reduce) {
      .nav a:active {
        transform: none;
      }
    }
    .nav a:focus-visible {
      outline: var(--focus-ring);
      outline-offset: -2px;
    }
    .count {
      margin-left: auto;
      color: var(--text);
      font-size: var(--fs-small);
      font-weight: 500;
      font-variant-numeric: tabular-nums;
    }
    .nav a.is-current {
      background: var(--active);
      color: var(--text);
      font-weight: 500;
    }
    .links {
      display: flex;
      flex: 1;
      flex-direction: column;
      gap: 2px;
      min-height: 0;
    }
    .foot {
      display: flex;
      flex-direction: column;
      gap: 2px;
      margin-top: auto;
      padding-top: var(--space-md);
    }
    :host(.is-menu) .nav a {
      height: 44px;
    }
    @media (hover: hover) and (pointer: fine) {
      .nav a:hover {
        background: color-mix(in srgb, var(--active) 60%, transparent);
        color: var(--text);
      }
      .nav a.is-current:hover {
        background: var(--active);
      }
    }
  `,
  host: { '[class.is-menu]': 'menuMode()' },
})
export class Sidebar {
  protected readonly icons = AREA_ICONS;
  readonly user = input<Me | null>(null);
  readonly organization = input<SessionOrganization | null>(null);
  readonly noticeCount = input<string | null>(null);
  readonly menuMode = input(false, { transform: booleanAttribute });
  readonly closeMenu = output();
  readonly navigated = output();

  private readonly orgLinks = computed<NavLink[]>(() => {
    const slug = this.organization()?.slug;
    return AREAS.flatMap((area) => {
      const url = areaUrl(area, slug);
      return url ? [{ area, url }] : [];
    });
  });
  protected readonly mainLinks = computed(() =>
    this.orgLinks().filter((l) => l.area.group === 'main'),
  );
  protected readonly footLinks = computed(() => {
    const links = this.orgLinks().filter((l) => l.area.group === 'foot');
    if (this.user()?.isInstanceAdmin)
      links.push({ area: ADMINISTRATION, url: ADMINISTRATION.path });
    return links;
  });
}
