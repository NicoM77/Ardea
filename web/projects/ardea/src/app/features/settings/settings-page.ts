import { Component, computed } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { UiPageHeader } from 'ui';
import { injectOrganization } from '../organizations/current-organization';
import { canManage } from '../organizations/org-format';
import { MfaSettingCard } from '../team/mfa-setting-card';
import { AccessesCard } from './accesses-card';
import { AuditCard } from './audit-card';
import { GeneralCard } from './general-card';

/**
 * The organization's settings (master plan 11.4): name, language and time zone, the
 * two-factor requirement, the accesses to GitHub and, for owner and admins, the audit log.
 */
@Component({
  selector: 'ardea-settings-page',
  imports: [TranslocoPipe, UiPageHeader, AccessesCard, AuditCard, GeneralCard, MfaSettingCard],
  template: `
    @if (organization(); as org) {
      <ui-page-header
        [heading]="'nav.settings' | transloco"
        [sub]="'settings.lead' | transloco: { name: org.name }"
      />
      <div class="grid">
        <ardea-general-card class="span-7" [organization]="org" />
        @if (manage()) {
          <ardea-mfa-setting-card class="span-5" [organization]="org" [here]="here()" />
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
