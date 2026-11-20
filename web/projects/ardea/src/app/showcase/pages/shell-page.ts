import { Component } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { LucideLayoutGrid } from '@lucide/angular';
import { UiCard, UiEmptyState, UiPageHeader } from 'ui';
import type { Me } from '../../core/session/session';
import { Sidebar } from '../../layout/shell/sidebar';
import { Topbar } from '../../layout/shell/topbar';

/** Sample account: not a real organization or person. */
const SAMPLE_ME: Me = {
  id: 'sample-1',
  email: 'sample.person@example.org',
  displayName: 'Sample Person',
  isInstanceAdmin: true,
  twoFactorEnabled: false,
  organizations: [
    { id: 'org-1', slug: 'example', name: 'Example Org', role: 'owner', mfaRequired: false },
    { id: 'org-2', slug: 'sample', name: 'Sample Agency', role: 'member', mfaRequired: false },
  ],
};

/** The frame of the signed-in app, drawn with sample data: sidebar, top bar and an empty sheet. */
@Component({
  selector: 'ardea-shell-showcase-page',
  imports: [TranslocoPipe, LucideLayoutGrid, Sidebar, Topbar, UiCard, UiEmptyState, UiPageHeader],
  template: `
    <div class="sc-page">
      <ui-page-header
        heading="Shell"
        sub="228px sidebar and 48px top bar on the ground, the work on a white sheet."
      />

      <ui-card heading="Desktop">
        <div class="sc-frame shell">
          <ardea-sidebar class="side" [user]="me" [organization]="me.organizations[0]" />
          <ardea-topbar class="top" [user]="me" />
          <div class="sheet">
            <ui-page-header [heading]="'nav.overview' | transloco" />
            <ui-empty-state [heading]="'areas.overview.emptyHeading' | transloco">
              <svg uiEmptyIcon lucideLayoutGrid aria-hidden="true"></svg>
              {{ 'areas.overview.emptyText' | transloco }}
            </ui-empty-state>
          </div>
        </div>
      </ui-card>

      <ui-card heading="Phone menu">
        <div class="sc-frame phone">
          <ardea-sidebar [user]="me" [organization]="me.organizations[0]" menuMode />
        </div>
      </ui-card>
    </div>
  `,
  styles: `
    .shell {
      display: grid;
      grid-template-columns: var(--sidebar-width) minmax(0, 1fr);
      grid-template-rows: var(--topbar) auto;
      height: 560px;
    }
    .side {
      grid-row: 1 / 3;
    }
    .sheet {
      margin: 0 var(--space-sm) var(--space-sm) 0;
      padding: var(--pad);
      border: 1px solid var(--border);
      border-radius: var(--radius-sheet);
      background: var(--surface);
    }
    .phone {
      width: 390px;
      /* Tall enough for every area and the person at the foot */
      height: 720px;
    }
    .phone ardea-sidebar {
      height: 100%;
      padding: var(--space-md) var(--space-lg);
    }
  `,
})
export class ShellShowcasePage {
  protected readonly me = SAMPLE_ME;
}
