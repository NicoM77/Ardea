import { Component } from '@angular/core';
import {
  UI_PRIORITY_LEVELS,
  UI_STATUS_KINDS,
  UiAvatar,
  UiCard,
  UiPageHeader,
  UiPill,
  UiPriority,
  UiStatus,
} from 'ui';

/** The signature marks: priority, working state, signal pills and people. */
@Component({
  selector: 'ardea-signals-page',
  imports: [UiAvatar, UiCard, UiPageHeader, UiPill, UiPriority, UiStatus],
  template: `
    <div class="sc-page">
      <ui-page-header
        heading="Priority and status"
        sub="Priority is symbol, word and colour, never colour alone and never a numeral. Working states are neutral."
      />

      <div class="sc-grid">
        <ui-card heading="Priority">
          <div class="sc-rows">
            @for (level of levels; track level) {
              <div class="sc-row">
                <span class="sc-label">{{ level }}</span><ui-priority [level]="level" />
              </div>
            }
          </div>
        </ui-card>

        <ui-card heading="Working state">
          <div class="sc-rows">
            @for (kind of kinds; track kind) {
              <div class="sc-row">
                <span class="sc-label">{{ kind }}</span><ui-status [kind]="kind" />
              </div>
            }
          </div>
        </ui-card>

        <ui-card heading="Signals and people">
          <div class="sc-rows">
            <div class="sc-row">
              <span class="sc-label">Pills</span>
              <div class="sc-demo">
                <ui-pill tone="alert">Exploited</ui-pill>
                <ui-pill tone="done">Fix available</ui-pill>
                <ui-pill>No fix yet</ui-pill>
              </div>
            </div>
            <div class="sc-row">
              <span class="sc-label">Avatars</span>
              <div class="sc-demo">
                <ui-avatar name="Sample Person" personId="sample-1" />
                <ui-avatar name="Example User" personId="sample-2" />
                <ui-avatar name="Test Reviewer" personId="sample-3" />
              </div>
            </div>
          </div>
        </ui-card>
      </div>
    </div>
  `,
})
export class SignalsPage {
  protected readonly levels = UI_PRIORITY_LEVELS;
  protected readonly kinds = UI_STATUS_KINDS;
}
