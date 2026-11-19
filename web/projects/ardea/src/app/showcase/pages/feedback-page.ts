import { Component } from '@angular/core';
import { LucideFolder, LucideListChecks } from '@lucide/angular';
import {
  UiButton,
  UiCard,
  UiCopyButton,
  UiEmptyState,
  UiNotice,
  UiPageHeader,
  UiLoader,
  UiToast,
} from 'ui';

/** Notices, empty states, loading and copy feedback. */
@Component({
  selector: 'ardea-feedback-page',
  imports: [
    LucideFolder,
    LucideListChecks,
    UiButton,
    UiCard,
    UiCopyButton,
    UiEmptyState,
    UiNotice,
    UiPageHeader,
    UiLoader,
    UiToast,
  ],
  template: `
    <div class="sc-page">
      <ui-page-header
        heading="Feedback"
        sub="Sentences about a page or a form, empty states and progress."
      />

      <ui-card heading="Notices">
        <div class="sc-stack" style="max-width: 560px">
          <ui-notice tone="info">The link in the email is valid for a limited time.</ui-notice>
          <ui-notice tone="error">The email address or the password is not right.</ui-notice>
          <ui-notice tone="success">Your password is changed. Sign in with the new one.</ui-notice>
          <ui-notice tone="error" heading="This invitation link is incomplete">
            Ask the person who invited you to send it again.
          </ui-notice>
          <ui-notice tone="success" heading="Check your inbox">
            If an account exists for name@example.org, a link to choose a new password is on its
            way.
          </ui-notice>
        </div>
      </ui-card>

      <div class="sc-grid">
        <ui-empty-state heading="No open actions">
          <svg uiEmptyIcon lucideListChecks aria-hidden="true"></svg>
          An action appears when a vulnerability affects a component of one of your projects.
        </ui-empty-state>
        <ui-empty-state heading="No projects yet">
          <svg uiEmptyIcon lucideFolder aria-hidden="true"></svg>
          A project describes what you run, so Ardea can tell which vulnerabilities affect it.
          <button uiEmptyActions uiButton type="button">Add a project</button>
        </ui-empty-state>
        <ui-empty-state heading="Nothing matches these filters">
          Clear a filter to see more actions.
        </ui-empty-state>
      </div>

      <ui-card heading="Confirmation">
        <span uiCardMeta>A dark toast at the bottom, with Undo where there is a way back</span>
        <div class="sc-stack">
          <div><ui-toast text="Marked as remediated" [undoable]="true" /></div>
          <div><ui-toast text="Note saved" /></div>
        </div>
      </ui-card>

      <ui-card heading="Loader">
        <span uiCardMeta>Draw, 2.4 s per cycle, shown after 300 ms</span>
        <div class="sc-rows">
          <div class="sc-row">
            <span class="sc-label">Page · 96</span>
            <div class="sc-demo">
              <ui-loader size="page" label="Loading entries" [delay]="0" />
            </div>
          </div>
          <div class="sc-row">
            <span class="sc-label">Inline · 20, button · 16</span>
            <div class="sc-demo">
              <ui-loader label="Fetching CVE records" [delay]="0" />
              <button uiButton type="button" loading>Syncing sources</button>
              <button uiButton variant="primary" type="button" loading>Save</button>
            </div>
          </div>
          <div class="sc-row">
            <span class="sc-label">Frames of the loop</span>
            <div class="sc-demo">
              @for (frame of frames; track frame) {
                <ui-loader size="page" [label]="frame * 100 + ' %'" [delay]="0" [frame]="frame" />
              }
            </div>
          </div>
          <div class="sc-row">
            <span class="sc-label">Done</span>
            <div class="sc-demo">
              <ui-loader size="page" label="Loaded" [delay]="0" done />
            </div>
          </div>
        </div>
      </ui-card>

      <ui-card heading="Copy">
        <div class="sc-rows">
          <div class="sc-row">
            <span class="sc-label">Copy</span>
            <div class="sc-demo">
              <ui-copy-button text="JBSW Y3DP EHPK 3PXP" label="Copy key" />
              <ui-copy-button text="JBSW Y3DP EHPK 3PXP" label="Copy key" variant="ghost" />
            </div>
          </div>
        </div>
      </ui-card>
    </div>
  `,
  styles: `
    .muted {
      color: var(--muted);
    }
  `,
})
export class FeedbackPage {
  protected readonly frames = [0.25, 0.5, 0.75, 1];
}
