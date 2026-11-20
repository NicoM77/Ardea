import { Component, computed } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { UiButton, UiCard, UiLoader, UiNotice, UiPageHeader } from 'ui';
import {
  getInstanceResource,
  getInstanceSettingsResource,
  getInstanceStatusResource,
  listAbandonedJobsResource,
  type InstanceSettingsResponse,
} from '../../core/api/client';
import { AiCard } from './ai-card';
import { JobsCard } from './jobs-card';
import { OperationsCard } from './operations-card';
import { RegistrationCard } from './registration-card';
import { count } from './settings-patch';
import { SmtpCard } from './smtp-card';

@Component({
  selector: 'ardea-admin-page',
  imports: [
    TranslocoPipe,
    AiCard,
    JobsCard,
    OperationsCard,
    RegistrationCard,
    SmtpCard,
    UiButton,
    UiCard,
    UiLoader,
    UiNotice,
    UiPageHeader,
  ],
  template: `
    <ui-page-header [heading]="'admin.title' | transloco" [sub]="'admin.sub' | transloco" />

    @if (firstLoad()) {
      <div class="wait" aria-busy="true">
        <ui-loader size="page" [label]="'admin.loading' | transloco" />
      </div>
    } @else {
      <div class="grid">
        <ui-card class="span-12" [heading]="'admin.instance.title' | transloco">
          @if (instance.hasValue() && instance.value(); as info) {
            <dl class="facts">
              <div>
                <dt>{{ 'admin.instance.version' | transloco }}</dt>
                <dd class="id">{{ info.version }}</dd>
              </div>
              <div>
                <dt>{{ 'admin.instance.pending' | transloco }}</dt>
                <dd>{{ number(info.jobs.pending) }}</dd>
              </div>
              <div>
                <dt>{{ 'admin.instance.running' | transloco }}</dt>
                <dd>{{ number(info.jobs.running) }}</dd>
              </div>
              <div>
                <dt>{{ 'admin.instance.failed' | transloco }}</dt>
                <dd>{{ number(info.jobs.failed) }}</dd>
              </div>
              <div>
                <dt>{{ 'admin.instance.dead' | transloco }}</dt>
                <dd>{{ number(info.jobs.dead) }}</dd>
              </div>
            </dl>
          } @else if (instance.error()) {
            <ui-notice tone="error">{{ 'admin.instance.loadFailed' | transloco }}</ui-notice>
            <div class="gap">
              <button uiButton type="button" (click)="instance.reload()">
                {{ 'admin.tryAgain' | transloco }}
              </button>
            </div>
          } @else {
            <ui-loader [label]="'admin.loading' | transloco" />
          }
        </ui-card>

        @if (settings.hasValue() && settings.value(); as value) {
          <ardea-smtp-card class="span-7" [settings]="value" (changed)="stored($event)" />
          <ardea-registration-card class="span-5" [settings]="value" (changed)="stored($event)" />
        } @else if (settings.error()) {
          <ui-card class="span-12" [heading]="'admin.settingsTitle' | transloco">
            <ui-notice tone="error">{{ 'admin.settingsLoadFailed' | transloco }}</ui-notice>
            <div class="gap">
              <button uiButton type="button" (click)="settings.reload()">
                {{ 'admin.tryAgain' | transloco }}
              </button>
            </div>
          </ui-card>
        } @else {
          <ui-card class="span-12" [heading]="'admin.settingsTitle' | transloco">
            <ui-loader [label]="'admin.loading' | transloco" />
          </ui-card>
        }

        <ardea-operations-card
          class="span-12"
          [status]="status.hasValue() ? status.value() : null"
          [loading]="status.isLoading()"
          [failed]="!!status.error()"
          (reload)="status.reload()"
        />

        <ardea-ai-card class="span-12" />

        <ardea-jobs-card
          class="span-12"
          [jobs]="jobs.hasValue() ? jobs.value() : null"
          [loading]="jobs.isLoading()"
          [failed]="!!jobs.error()"
          (reload)="jobsChanged()"
        />
      </div>
    }
  `,
  styles: `
    .wait {
      display: grid;
      place-items: center;
      min-height: 320px;
    }
    .grid {
      display: grid;
      grid-template-columns: repeat(12, minmax(0, 1fr));
      gap: var(--gap);
      align-items: start;
    }
    .span-12 {
      grid-column: span 12;
    }
    .span-7 {
      grid-column: span 7;
    }
    .span-5 {
      grid-column: span 5;
    }
    .facts {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-lg) var(--space-2xl);
    }
    .facts dt {
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .facts dd {
      margin-top: 2px;
      font-size: var(--fs-section);
      line-height: var(--lh-section);
      font-weight: 500;
    }
    .facts dd.id {
      font-size: var(--fs-body);
      line-height: var(--lh-section);
    }
    .gap {
      margin-top: var(--space-md);
    }
    @media (max-width: 1100px) {
      .span-7,
      .span-5 {
        grid-column: span 12;
      }
    }
  `,
})
export class AdminPage {
  protected readonly instance = getInstanceResource();
  protected readonly settings = getInstanceSettingsResource();
  protected readonly jobs = listAbandonedJobsResource();
  protected readonly status = getInstanceStatusResource();
  protected readonly number = count;

  protected readonly firstLoad = computed(
    () =>
      this.instance.status() === 'loading' &&
      this.settings.status() === 'loading' &&
      this.jobs.status() === 'loading',
  );

  protected stored(value: InstanceSettingsResponse): void {
    this.settings.set(value);
  }

  protected jobsChanged(): void {
    this.jobs.reload();
    this.status.reload();
    this.instance.reload();
  }
}
