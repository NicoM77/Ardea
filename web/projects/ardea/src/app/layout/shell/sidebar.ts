import { booleanAttribute, Component, computed, input, output } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { LucideDynamicIcon, LucideX } from '@lucide/angular';
import { UiAvatar, UiButton } from 'ui';
import type { SessionOrganization } from '../../core/api/client';
import type { Me } from '../../core/session/session';
import { ADMINISTRATION, Area, AREAS } from '../../features/areas/areas';
import { OrgSwitch } from './org-switch';

interface NavLink {
  area: Area;
  url: string;
}

/**
 * The sidebar on the ground (DESIGN.md, Navigation): logo, organization switch, the areas at
 * 32px with 16px icons, the selected one on an Active fill, and the person at the foot.
 * Below 900px the shell shows it as a full-screen menu; `menuMode` adds the close button.
 */
@Component({
  selector: 'ardea-sidebar',
  imports: [
    RouterLink,
    RouterLinkActive,
    TranslocoPipe,
    LucideDynamicIcon,
    LucideX,
    UiAvatar,
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

    <ul class="nav">
      @for (link of mainLinks(); track link.area.id) {
        <li>
          <a
            [routerLink]="link.url"
            routerLinkActive="is-current"
            ariaCurrentWhenActive="page"
            (click)="navigated.emit()"
          >
            <svg [lucideIcon]="link.area.icon" aria-hidden="true"></svg>
            <span>{{ link.area.label | transloco }}</span>
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
              <svg [lucideIcon]="link.area.icon" aria-hidden="true"></svg>
              <span>{{ link.area.label | transloco }}</span>
            </a>
          </li>
        }
      </ul>
      @if (user(); as me) {
        <div class="person">
          <ui-avatar [name]="me.displayName" [personId]="me.id" />
          <span class="person-name">
            {{ me.displayName }}
            <small>{{ me.email }}</small>
          </span>
        </div>
      }
    </div>
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
    .nav a:focus-visible {
      outline: var(--focus-ring);
      outline-offset: -2px;
    }
    .nav a.is-current {
      background: var(--active);
      color: var(--text);
      font-weight: 500;
    }
    .foot {
      display: flex;
      flex-direction: column;
      gap: 2px;
      margin-top: auto;
      padding-top: var(--space-md);
    }
    .person {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: var(--space-sm);
      margin-top: var(--space-xs);
    }
    .person-name {
      min-width: 0;
      font-weight: 500;
      line-height: var(--lh-small);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .person-name small {
      display: block;
      overflow: hidden;
      text-overflow: ellipsis;
      color: var(--muted);
      font-size: var(--fs-sub);
      font-weight: 400;
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
  readonly user = input<Me | null>(null);
  readonly organization = input<SessionOrganization | null>(null);
  /** Shown as the phone menu: larger targets and a close button. */
  readonly menuMode = input(false, { transform: booleanAttribute });
  readonly closeMenu = output();
  readonly navigated = output();

  private readonly orgLinks = computed<NavLink[]>(() => {
    const org = this.organization();
    if (!org) return [];
    return AREAS.map((area) => ({ area, url: `/o/${org.slug}/${area.path}` }));
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
