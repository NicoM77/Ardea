import { Component, computed, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { LucideDynamicIcon } from '@lucide/angular';
import { UiEmptyState, UiPageHeader } from 'ui';
import { AREA_ICONS } from './area-icons';
import { AreaId, areaById } from './areas';

@Component({
  selector: 'ardea-area-page',
  imports: [TranslocoPipe, LucideDynamicIcon, UiEmptyState, UiPageHeader],
  template: `
    <ui-page-header [heading]="def().label | transloco" />
    <ui-empty-state [heading]="def().emptyHeading | transloco">
      <svg uiEmptyIcon [lucideIcon]="icons[def().id]" aria-hidden="true"></svg>
      {{ def().emptyText | transloco }}
    </ui-empty-state>
  `,
})
export class AreaPage {
  readonly area = input.required<AreaId>();
  protected readonly def = computed(() => areaById(this.area()));
  protected readonly icons = AREA_ICONS;
}
