import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { LucideShieldCheck } from '@lucide/angular';
import { UiButton, UiEmptyState, UiPageHeader } from 'ui';

/**
 * In place of an organization's pages while it asks for two-factor authentication and the
 * person has not turned it on (the API answers `mfa_required`). One way forward: the account's
 * two-factor page, which returns here afterwards.
 */
@Component({
  selector: 'ardea-mfa-required',
  imports: [RouterLink, TranslocoPipe, LucideShieldCheck, UiButton, UiEmptyState, UiPageHeader],
  template: `
    <ui-page-header [heading]="'team.mfaRequired.title' | transloco" [sub]="organization()" />
    <ui-empty-state [heading]="'team.mfaRequired.heading' | transloco: { name: organization() }">
      <svg uiEmptyIcon lucideShieldCheck aria-hidden="true"></svg>
      {{ 'team.mfaRequired.text' | transloco }}
      <a
        uiEmptyActions
        uiButton
        variant="primary"
        routerLink="/account/two-factor"
        [queryParams]="{ returnUrl: returnUrl() }"
        >{{ 'team.mfaRequired.action' | transloco }}</a
      >
    </ui-empty-state>
  `,
})
export class MfaRequired {
  readonly organization = input.required<string>();
  /** Where the two-factor page sends the person once it is on. */
  readonly returnUrl = input.required<string>();
}
