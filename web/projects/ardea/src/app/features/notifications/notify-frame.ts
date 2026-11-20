import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { UiNotice, UiPageHeader } from 'ui';
import type { SessionOrganization } from '../../core/api/client';
import { canManage } from '../organizations/org-format';

export type NotifyTab = 'rules' | 'recipients' | 'log';

@Component({
  selector: 'ardea-notify-frame',
  imports: [RouterLink, TranslocoPipe, UiNotice, UiPageHeader],
  template: `
    <ui-page-header [heading]="'nav.rules' | transloco" [sub]="sub()">
      <ng-container ngProjectAs="[uiPageActions]"
        ><ng-content select="[uiPageActions]"
      /></ng-container>
    </ui-page-header>
    @if (manage()) {
      <nav class="tabs" [attr.aria-label]="'nav.rules' | transloco">
        @for (tab of tabs; track tab.id) {
          <a
            [routerLink]="base() + tab.path"
            [attr.aria-current]="tab.id === current() ? 'page' : null"
            >{{ tab.label | transloco }}</a
          >
        }
      </nav>
      <ng-content />
    } @else {
      <ui-notice [heading]="'notify.adminsOnly.heading' | transloco">
        {{ 'notify.adminsOnly.text' | transloco }}
        <a class="link" [routerLink]="'/o/' + organization().slug + '/settings/notifications'">{{
          'notify.adminsOnly.link' | transloco
        }}</a>
      </ui-notice>
    }
  `,
  styles: `
    :host {
      display: block;
    }
    .tabs {
      display: flex;
      gap: var(--gap);
      margin-bottom: var(--gap);
      border-bottom: 1px solid var(--border);
    }
    .tabs a {
      display: inline-flex;
      align-items: center;
      height: 40px;
      margin-bottom: -1px;
      border-bottom: 2px solid transparent;
      color: var(--muted);
    }
    .tabs a[aria-current='page'] {
      border-bottom-color: var(--text);
      color: var(--text);
      font-weight: 500;
    }
    .tabs a:focus-visible {
      outline: var(--focus-ring);
      outline-offset: var(--focus-offset);
    }
    @media (hover: hover) and (pointer: fine) {
      .tabs a:hover {
        color: var(--text);
      }
    }
  `,
})
export class NotifyFrame {
  readonly organization = input.required<SessionOrganization>();
  readonly current = input.required<NotifyTab>();
  readonly sub = input<string | null>(null);

  protected readonly manage = computed(() => canManage(this.organization().role));
  protected readonly base = computed(() => `/o/${this.organization().slug}/notification-rules`);
  protected readonly tabs = [
    { id: 'rules', path: '', label: 'notify.tabs.rules' },
    { id: 'recipients', path: '/recipients', label: 'notify.tabs.recipients' },
    { id: 'log', path: '/log', label: 'notify.tabs.log' },
  ] as const;
}
