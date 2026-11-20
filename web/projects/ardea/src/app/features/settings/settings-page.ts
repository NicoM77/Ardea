import { Component, computed } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { RouterLink } from '@angular/router';
import { UiButton, UiCard, UiPageHeader } from 'ui';
import { injectOrganization } from '../organizations/current-organization';
import { canManage } from '../organizations/org-format';
import { MfaSettingCard } from '../team/mfa-setting-card';
import { AccessesCard } from './accesses-card';
import { AiPolicyCard } from './ai-policy-card';
import { AuditCard } from './audit-card';
import { GeneralCard } from './general-card';

@Component({
  selector: 'ardea-settings-page',
  imports: [
    RouterLink,
    TranslocoPipe,
    UiButton,
    UiCard,
    UiPageHeader,
    AccessesCard,
    AiPolicyCard,
    AuditCard,
    GeneralCard,
    MfaSettingCard,
  ],
  template: `
    @if (organization(); as org) {
      <ui-page-header
        [heading]="'nav.settings' | transloco"
        [sub]="'settings.lead' | transloco: { name: org.name }"
      />
      <div class="grid">
        <ardea-general-card class="span-7" [organization]="org" />
        <ui-card class="span-5" [heading]="'notify.settings.title' | transloco">
          <p class="hint">{{ 'settings.notifications.text' | transloco }}</p>
          <a uiButton class="go" [routerLink]="'/o/' + org.slug + '/settings/notifications'">
            {{ 'settings.notifications.open' | transloco }}
          </a>
        </ui-card>
        @if (manage()) {
          <ardea-mfa-setting-card class="span-5" [organization]="org" [here]="here()" />
          <ardea-ai-policy-card class="span-12" [organization]="org" />
        }
        <ardea-accesses-card class="span-12" [organization]="org" />
        @if (manage()) {
          <ardea-audit-card class="span-12" [organization]="org" />
        }
      </div>
    }
  `,
  styles: `
    :host {
      display: block;
    }
    .grid {
      display: grid;
      grid-template-columns: repeat(12, minmax(0, 1fr));
      gap: var(--gap);
      align-items: start;
    }
    .hint {
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .go {
      margin-top: var(--space-md);
    }
    .span-12 {
      grid-column: span 12;
    }
    .span-7 {
      grid-column: span 7;
    }
    .span-5 {
      grid-column: span 5;
    }
    @media (max-width: 1100px) {
      .span-7,
      .span-5 {
        grid-column: span 12;
      }
    }
  `,
})
export class SettingsPage {
  private readonly current = injectOrganization();
  protected readonly organization = this.current.organization;
  protected readonly manage = computed(() => canManage(this.organization()?.role));
  protected readonly here = computed(() => `/o/${this.current.slug()}/settings`);
}
