import { CdkMenu, CdkMenuItem, CdkMenuTrigger } from '@angular/cdk/menu';
import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { LucideCheck, LucideChevronsUpDown, LucidePlus } from '@lucide/angular';
import type { SessionOrganization } from '../../core/api/client';
import { ROLE_LABELS } from '../../features/organizations/org-format';

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
    LucidePlus,
  ],
  template: `
    @if (current(); as org) {
      <button type="button" class="switch" [cdkMenuTriggerFor]="menu">
        <span class="mark" aria-hidden="true">{{ mark() }}</span>
        <span class="name">
          <span class="sr-only">{{ 'shell.org.switchPrefix' | transloco }}</span>
          {{ org.name }}
          @if (roleLabel(); as label) {
            <small>{{ label | transloco }}</small>
          }
          <span class="sr-only">{{ 'shell.org.switchHint' | transloco }}</span>
        </span>
        <svg lucideChevronsUpDown aria-hidden="true"></svg>
      </button>
    } @else {
      <a class="switch is-empty" routerLink="/organizations/new">
        <span class="mark" aria-hidden="true">–</span>
        <span class="name">
          {{ 'shell.org.none' | transloco }}
          <small>{{ 'shell.org.createShort' | transloco }}</small>
        </span>
        <svg lucidePlus aria-hidden="true"></svg>
      </a>
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
        <div class="ui-menu-sep" role="separator"></div>
        <a class="ui-menu-item" cdkMenuItem routerLink="/organizations/new">
          <svg lucidePlus aria-hidden="true"></svg>
          <span class="grow">{{ 'shell.org.create' | transloco }}</span>
        </a>
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
    .switch:active {
      transform: scale(0.98);
    }
    @media (prefers-reduced-motion: reduce) {
      .switch:active {
        transform: none;
      }
    }
    .switch:focus-visible {
      outline: var(--focus-ring);
      outline-offset: var(--focus-offset);
    }
    .switch.is-empty {
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
      .switch:hover {
        border-color: var(--control);
      }
    }
  `,
})
export class OrgSwitch {
  readonly organizations = input.required<readonly SessionOrganization[]>();
  readonly current = input<SessionOrganization | null>(null);
  protected readonly roleLabel = computed(() => ROLE_LABELS[this.current()?.role ?? ''] ?? null);
  protected readonly mark = computed(() =>
    (Array.from(this.current()?.name.trim() ?? '')[0] ?? '').toLocaleUpperCase(),
  );
}
