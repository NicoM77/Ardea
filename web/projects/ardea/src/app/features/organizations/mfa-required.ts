import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { LucideShieldCheck } from '@lucide/angular';
import { UiButton, UiEmptyState, UiPageHeader } from 'ui';

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
  readonly returnUrl = input.required<string>();
}
