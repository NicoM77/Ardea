import { Component, computed, inject } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { LucideBuilding } from '@lucide/angular';
import { UiEmptyState, UiPageHeader } from 'ui';
import { Session } from '../../core/session/session';

/** Signed in, but not a member of any organization yet: after setup, or before an invitation. */
@Component({
  selector: 'ardea-no-organization-page',
  imports: [TranslocoPipe, LucideBuilding, UiEmptyState, UiPageHeader],
  template: `
    <ui-page-header [heading]="'home.noOrganization.title' | transloco" />
    <ui-empty-state [heading]="'home.noOrganization.heading' | transloco">
      <svg uiEmptyIcon lucideBuilding aria-hidden="true"></svg>
      @if (isAdmin()) {
        {{ 'home.noOrganization.textAdmin' | transloco }}
      } @else {
        {{ 'home.noOrganization.text' | transloco }}
      }
    </ui-empty-state>
  `,
})
export class NoOrganizationPage {
  private readonly session = inject(Session);
  protected readonly isAdmin = computed(() => this.session.user()?.isInstanceAdmin === true);
}
