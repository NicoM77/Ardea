import { Component } from '@angular/core';
import { LucideFolder, LucideListChecks } from '@lucide/angular';
import {
  UiButton,
  UiCard,
  UiCopyButton,
  UiEmptyState,
  UiNotice,
  UiPageHeader,
  UiSpinner,
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
    UiSpinner,
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

      <ui-card heading="Progress and copy">
        <div class="sc-rows">
          <div class="sc-row">
            <span class="sc-label">Spinner</span>
            <div class="sc-demo">
              <ui-spinner />
              <span class="muted"
                >Loading on its own; inside a button it sits beside the label.</span
              >
            </div>
          </div>
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
export class FeedbackPage {}
