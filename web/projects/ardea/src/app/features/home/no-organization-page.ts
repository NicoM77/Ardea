import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { LucideBuilding, LucidePlus } from '@lucide/angular';
import { UiButton, UiEmptyState, UiPageHeader } from 'ui';

/**
 * Signed in, but not a member of any organization yet: after setup, or after leaving the last
 * one. Anyone may create an organization; joining an existing one takes an invitation.
 */
@Component({
  selector: 'ardea-no-organization-page',
  imports: [
    RouterLink,
    TranslocoPipe,
    LucideBuilding,
    LucidePlus,
    UiButton,
    UiEmptyState,
    UiPageHeader,
  ],
  template: `
    <ui-page-header [heading]="'home.noOrganization.title' | transloco" />
    <ui-empty-state [heading]="'home.noOrganization.heading' | transloco">
      <svg uiEmptyIcon lucideBuilding aria-hidden="true"></svg>
      {{ 'home.noOrganization.text' | transloco }}
      <a uiEmptyActions uiButton variant="primary" routerLink="/organizations/new">
        <svg lucidePlus aria-hidden="true"></svg>
        {{ 'home.noOrganization.create' | transloco }}
      </a>
    </ui-empty-state>
  `,
})
export class NoOrganizationPage {}
