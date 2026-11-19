import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { LucideDynamicIcon } from '@lucide/angular';
import { UiButton, UiEmptyState, UiPageHeader } from 'ui';
import { getWizardStateResource } from '../../core/api/client';
import { injectOrganization } from '../organizations/current-organization';
import { roleRank } from '../organizations/org-format';
import { AreaId, areaById } from './areas';

/**
 * The page of a navigation area until its feature is built: its title and an empty state that
 * says what will appear here. No sample data. The overview of an organization whose setup is not
 * finished offers to continue it (master plan 11.6).
 */
@Component({
  selector: 'ardea-area-page',
  imports: [RouterLink, TranslocoPipe, LucideDynamicIcon, UiButton, UiEmptyState, UiPageHeader],
  template: `
    <ui-page-header [heading]="def().label | transloco" />
    @if (offerSetup()) {
      <ui-empty-state [heading]="'wizard.prompt.heading' | transloco">
        <svg uiEmptyIcon [lucideIcon]="def().icon" aria-hidden="true"></svg>
        {{ 'wizard.prompt.text' | transloco }}
        <a uiEmptyActions uiButton variant="primary" routerLink="../setup">
          {{ 'wizard.prompt.action' | transloco }}
        </a>
      </ui-empty-state>
    } @else {
      <ui-empty-state [heading]="def().emptyHeading | transloco">
        <svg uiEmptyIcon [lucideIcon]="def().icon" aria-hidden="true"></svg>
        {{ def().emptyText | transloco }}
      </ui-empty-state>
    }
  `,
})
export class AreaPage {
  /** From the route's data. */
  readonly area = input.required<AreaId>();
  protected readonly def = computed(() => areaById(this.area()));

  private readonly organization = injectOrganization().organization;
  private readonly setup = getWizardStateResource(
    computed(() => (this.area() === 'overview' ? this.organization()?.id : undefined)),
  );
  protected readonly offerSetup = computed(
    () =>
      roleRank(this.organization()?.role) >= roleRank('member') &&
      this.setup.hasValue() &&
      !this.setup.value()?.completedAt,
  );
}
