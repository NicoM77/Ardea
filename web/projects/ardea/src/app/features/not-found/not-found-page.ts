import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { LucideCompass } from '@lucide/angular';
import { UiButton, UiEmptyState, UiPageHeader } from 'ui';

@Component({
  selector: 'ardea-not-found-page',
  imports: [RouterLink, TranslocoPipe, LucideCompass, UiButton, UiEmptyState, UiPageHeader],
  template: `
    <ui-page-header [heading]="'notFound.title' | transloco" />
    <ui-empty-state [heading]="'notFound.heading' | transloco">
      <svg uiEmptyIcon lucideCompass aria-hidden="true"></svg>
      {{ 'notFound.text' | transloco }}
      <a uiEmptyActions uiButton routerLink="/">{{ 'notFound.home' | transloco }}</a>
    </ui-empty-state>
  `,
})
export class NotFoundPage {}
