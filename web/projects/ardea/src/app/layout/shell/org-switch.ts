import { CdkMenu, CdkMenuItem, CdkMenuTrigger } from '@angular/cdk/menu';
import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { LucideCheck, LucideChevronsUpDown } from '@lucide/angular';
import type { SessionOrganization } from '../../core/api/client';

/**
 * The organization the pages belong to, and a menu to switch to another one. With no membership
 * it says so instead of offering an empty menu.
 */
@Component({
  selector: 'ardea-org-switch',
  imports: [
    CdkMenu,
    CdkMenuItem,
    CdkMenuTrigger,
    RouterLink,
    TranslocoPipe,
    LucideCheck,
    LucideChevronsUpDown,
  ],
  template: `
    @if (current(); as org) {
      <button
        type="button"
        class="switch"
        [cdkMenuTriggerFor]="menu"
        [attr.aria-label]="'shell.org.switchLabel' | transloco: { name: org.name }"
      >
        <span class="mark" aria-hidden="true">{{ mark() }}</span>
        <span class="name">
          {{ org.name }}
          <small>{{ 'shell.org.count' | transloco: { count: organizations().length } }}</small>
        </span>
        <svg lucideChevronsUpDown aria-hidden="true"></svg>
      </button>
    } @else {
      <div class="switch is-empty">
        <span class="mark" aria-hidden="true">–</span>
        <span class="name">{{ 'shell.org.none' | transloco }}</span>
      </div>
    }

    <ng-template #menu>
      <div class="ui-menu" cdkMenu>
        <p class="ui-menu-label">{{ 'shell.org.menuLabel' | transloco }}</p>
        @for (org of organizations(); track org.id) {
          <a
            class="ui-menu-item"
            cdkMenuItem
            [class.is-current]="org.slug === current()?.slug"
            [routerLink]="['/o', org.slug, 'overview']"
            [attr.aria-current]="org.slug === current()?.slug ? 'true' : null"
          >
            <span class="grow">{{ org.name }}</span>
            @if (org.slug === current()?.slug) {
              <svg lucideCheck aria-hidden="true"></svg>
            }
          </a>
        }
      </div>
    </ng-template>
  `,
  styles: `
    :host {
      display: block;
      margin-bottom: var(--space-md);
    }
    .switch {
      display: flex;
      align-items: center;
      gap: 10px;
      width: 100%;
      height: 44px;
      padding: 0 var(--space-sm);
      border: 1px solid var(--border);
      border-radius: var(--radius-card);
      background: var(--surface);
      color: var(--text);
      text-align: left;
      cursor: pointer;
      transition:
        border-color var(--t-hover) ease,
        transform var(--t-press) var(--ease-out);
    }
    .switch:active:not(.is-empty) {
      transform: scale(0.98);
    }
    .switch:focus-visible {
      outline: var(--focus-ring);
      outline-offset: var(--focus-offset);
    }
    .switch.is-empty {
      cursor: default;
      color: var(--muted);
    }
    .mark {
      display: grid;
      place-items: center;
      width: 24px;
      height: 24px;
      flex: none;
      border-radius: var(--radius-control);
      background: var(--accent);
      color: var(--primary-ink);
      font-size: var(--fs-small);
      font-weight: 600;
    }
    .is-empty .mark {
      background: var(--raised);
      color: var(--faint);
    }
    .name {
      flex: 1;
      min-width: 0;
      font-weight: 500;
      line-height: var(--lh-small);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    .name small {
      display: block;
      color: var(--muted);
      font-size: var(--fs-sub);
      font-weight: 400;
    }
    svg {
      color: var(--faint);
    }
    .grow {
      flex: 1;
      min-width: 0;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    @media (hover: hover) and (pointer: fine) {
      .switch:not(.is-empty):hover {
        border-color: var(--control);
      }
    }
  `,
})
export class OrgSwitch {
  readonly organizations = input.required<readonly SessionOrganization[]>();
  readonly current = input<SessionOrganization | null>(null);
  protected readonly mark = computed(() =>
    (Array.from(this.current()?.name.trim() ?? '')[0] ?? '').toLocaleUpperCase(),
  );
}
