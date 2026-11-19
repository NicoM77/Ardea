import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { LucideBell, LucideChevronRight } from '@lucide/angular';
import { UiButton, UiCard, UiInput, UiLoader, UiNotice, UiStatus } from 'ui';
import { firstValueFrom } from 'rxjs';
import {
  getNotificationPreferencesResource,
  listPushSubscriptionsResource,
  NotificationsService,
  type PushSubscriptionView,
} from '../../core/api/client';
import { problemMessageKey } from '../../core/api/problem';
import { Session } from '../../core/session/session';
import { num } from '../actions/action-format';
import { injectOrganization } from '../organizations/current-organization';
import { LOCALES } from '../settings/general-card';
import { CHANNEL_LABELS, PERSONAL_CHANNELS, type Channel } from './notify-format';
import { NotifyIcon } from './notify-icon';
import { PushDevice } from './push-device';

const CHANNEL_HINTS: Readonly<Record<string, string>> = {
  in_app: 'notify.settings.hints.inApp',
  email: 'notify.settings.hints.email',
  push: 'notify.settings.hints.push',
};

/**
 * The member's own notification settings in one organization (master plan 8.3, 11.4, 11.9; tasks 5.5, 5.8;
 * mockup notification-settings.html), linked from every e-mail: the channels they hear on, the language of
 * messages, push on this device with the Home Screen guide for iPhone and iPad, and their devices.
 */
@Component({
  selector: 'ardea-notification-settings-page',
  imports: [
    RouterLink,
    TranslocoPipe,
    LucideBell,
    LucideChevronRight,
    NotifyIcon,
    UiButton,
    UiCard,
    UiInput,
    UiLoader,
    UiNotice,
    UiStatus,
  ],
  template: `
    @if (organization(); as org) {
      <nav class="crumbs" [attr.aria-label]="'notify.editor.breadcrumb' | transloco">
        <a [routerLink]="'/o/' + org.slug + '/settings'">{{ 'nav.settings' | transloco }}</a>
        <svg lucideChevronRight aria-hidden="true"></svg>
        <span aria-current="page">{{ 'notify.settings.title' | transloco }}</span>
      </nav>
      <h1>{{ 'notify.settings.title' | transloco }}</h1>
      <p class="lead">{{ 'notify.settings.lead' | transloco: { name: org.name } }}</p>

      <div class="grid">
        <div class="col-7">
          <ui-card [heading]="'notify.settings.channels' | transloco">
            <div class="live" role="status">
              @if (saved()) {
                <ui-notice tone="success">{{ 'notify.settings.saved' | transloco }}</ui-notice>
              }
              @if (problem()) {
                <ui-notice tone="error">{{ problem() | transloco }}</ui-notice>
              }
            </div>
            @if (preferences.hasValue()) {
              <ul class="channels">
                @for (c of channels; track c) {
                  <li>
                    @switch (c) {
                      @case ('in_app') {
                        <svg lucideBell class="ic" aria-hidden="true"></svg>
                      }
                      @case ('email') {
                        <ardea-notify-icon class="ic" name="mail" />
                      }
                      @default {
                        <ardea-notify-icon class="ic" name="smartphone" />
                      }
                    }
                    <span class="main" [id]="'ch-' + c">
                      <b>{{ channelLabels[c] | transloco }}</b>
                      <small>{{ channelHint(c) }}</small>
                    </span>
                    <button
                      type="button"
                      role="switch"
                      class="switch"
                      [attr.aria-checked]="!muted().includes(c)"
                      [attr.aria-label]="channelLabels[c] | transloco"
                      [disabled]="saving()"
                      (click)="toggle(c)"
                    ></button>
                  </li>
                }
              </ul>
              <ui-notice class="note" [heading]="'notify.settings.criticalHeading' | transloco">
                {{
                  (canMuteCritical()
                    ? 'notify.settings.criticalMutable'
                    : 'notify.settings.criticalAlways'
                  ) | transloco: { name: org.name }
                }}
              </ui-notice>
            } @else if (preferences.error()) {
              <ui-notice tone="error">{{ 'notify.loadFailed' | transloco }}</ui-notice>
            } @else {
              <ui-loader [label]="'notify.loading' | transloco" />
            }
          </ui-card>

          <ui-card [heading]="'notify.settings.language' | transloco">
            <div class="field">
              <label class="label" for="message-locale">{{
                'notify.settings.languageLabel' | transloco
              }}</label>
              <select
                uiInput
                id="message-locale"
                [disabled]="saving()"
                (change)="setLocale($any($event.target).value)"
              >
                <option value="" [selected]="!locale()">
                  {{ 'notify.settings.languageOrganization' | transloco }}
                </option>
                @for (l of locales; track l.value) {
                  <option [value]="l.value" [attr.lang]="l.value" [selected]="locale() === l.value">
                    {{ l.name }}
                  </option>
                }
              </select>
            </div>
          </ui-card>
        </div>

        <div class="col-5 first">
          <ui-card [heading]="'notify.push.title' | transloco">
            <div class="device" [attr.aria-busy]="push.state() === 'checking' || pushBusy()">
              <div class="live" role="status">
                @if (pushProblem()) {
                  <ui-notice tone="error">{{ pushProblem() | transloco }}</ui-notice>
                }
              </div>
              @switch (push.state()) {
                @case ('checking') {
                  <ui-loader [label]="'notify.loading' | transloco" />
                }
                @case ('on') {
                  <p class="state"><ui-status kind="remediated" word="notify.push.on" /></p>
                  <p class="text">{{ 'notify.push.onText' | transloco }}</p>
                  <div class="buttons">
                    <button uiButton type="button" [loading]="pushBusy()" (click)="disable()">
                      {{ 'notify.push.turnOff' | transloco }}
                    </button>
                  </div>
                }
                @case ('off') {
                  <p class="state"><ui-status kind="open" word="notify.push.off" /></p>
                  <p class="text">{{ 'notify.push.offText' | transloco }}</p>
                  <div class="buttons">
                    <button
                      uiButton
                      variant="primary"
                      type="button"
                      [loading]="pushBusy()"
                      (click)="enable()"
                    >
                      {{ 'notify.push.turnOn' | transloco }}
                    </button>
                  </div>
                }
                @case ('blocked') {
                  <p class="state"><ui-status kind="notAffected" word="notify.push.blocked" /></p>
                  <p class="text">{{ 'notify.push.blockedText' | transloco }}</p>
                }
                @case ('install') {
                  <p class="state strong">{{ 'notify.push.install' | transloco }}</p>
                  <p class="text">{{ 'notify.push.installText' | transloco }}</p>
                  <ol class="steps">
                    <li>
                      <span class="pict"><ardea-notify-icon name="share" /></span>
                      <span
                        ><b>{{ 'notify.push.step1' | transloco }}</b
                        ><small>{{ 'notify.push.step1Hint' | transloco }}</small></span
                      >
                    </li>
                    <li>
                      <span class="pict"><ardea-notify-icon name="square-plus" /></span>
                      <span
                        ><b>{{ 'notify.push.step2' | transloco }}</b
                        ><small>{{ 'notify.push.step2Hint' | transloco }}</small></span
                      >
                    </li>
                    <li>
                      <span class="pict app"
                        ><img src="brand/ardea-mark.svg" alt="" width="24" height="24"
                      /></span>
                      <span
                        ><b>{{ 'notify.push.step3' | transloco }}</b
                        ><small>{{ 'notify.push.step3Hint' | transloco }}</small></span
                      >
                    </li>
                  </ol>
                }
                @default {
                  <p class="state strong">{{ 'notify.push.unsupported' | transloco }}</p>
                  <p class="text">{{ 'notify.push.unsupportedText' | transloco }}</p>
                }
              }
            </div>
          </ui-card>

          <ui-card [heading]="'notify.devices.title' | transloco">
            <span uiCardMeta>{{ 'notify.devices.meta' | transloco }}</span>
            <div class="live" role="status">
              @if (deviceNotice(); as n) {
                <ui-notice [tone]="n.tone">{{ n.key | transloco: n.params }}</ui-notice>
              }
            </div>
            @if (deviceList(); as list) {
              @if (list.length) {
                <ul class="devices">
                  @for (d of list; track d.id) {
                    <li>
                      <ardea-notify-icon class="ic" name="smartphone" />
                      <span class="main">
                        <b>{{ d.label }}</b>
                        <small>{{ deviceLine(d) }}</small>
                      </span>
                      <span class="buttons">
                        <button
                          uiButton
                          variant="ghost"
                          type="button"
                          [loading]="busy() === 'test:' + d.id"
                          [attr.aria-label]="
                            'notify.devices.testLabel' | transloco: { name: d.label }
                          "
                          (click)="test(d)"
                        >
                          {{ 'notify.devices.test' | transloco }}
                        </button>
                        <button
                          uiButton
                          variant="ghost"
                          type="button"
                          [loading]="busy() === 'remove:' + d.id"
                          [attr.aria-label]="
                            'notify.devices.removeLabel' | transloco: { name: d.label }
                          "
                          (click)="remove(d)"
                        >
                          {{ 'notify.devices.remove' | transloco }}
                        </button>
                      </span>
                    </li>
                  }
                </ul>
              } @else {
                <p class="text">{{ 'notify.devices.none' | transloco }}</p>
              }
            } @else if (devices.error()) {
              <ui-notice tone="error">{{ 'notify.loadFailed' | transloco }}</ui-notice>
            } @else {
              <ui-loader [label]="'notify.loading' | transloco" />
            }
          </ui-card>
        </div>
      </div>
    }
  `,
  styles: `
    :host {
      display: block;
    }
    .crumbs {
      display: flex;
      align-items: center;
      gap: var(--space-xs);
      margin-bottom: var(--space-sm);
      color: var(--muted);
      font-size: var(--fs-sub);
    }
    .crumbs a {
      color: var(--muted);
    }
    .crumbs svg {
      width: 14px;
      height: 14px;
    }
    .lead {
      margin: var(--space-xs) 0 var(--space-xl);
      color: var(--muted);
    }
    .grid {
      display: grid;
      grid-template-columns: repeat(12, minmax(0, 1fr));
      gap: var(--gap);
      align-items: start;
    }
    .col-7,
    .col-5 {
      display: grid;
      gap: var(--gap);
      min-width: 0;
    }
    .col-7 {
      grid-column: span 7;
    }
    .col-5 {
      grid-column: span 5;
    }
    .live:empty {
      display: none;
    }
    .live {
      display: grid;
      gap: var(--space-sm);
      margin-bottom: var(--space-md);
    }
    .channels,
    .devices {
      display: grid;
    }
    .channels li,
    .devices li {
      display: flex;
      align-items: center;
      gap: var(--space-md);
      min-height: 60px;
      padding: var(--space-sm) 0;
    }
    .channels li + li,
    .devices li + li {
      border-top: 1px solid var(--rule);
    }
    .ic {
      flex: none;
      width: 18px;
      height: 18px;
      color: var(--muted);
    }
    .main {
      display: grid;
      flex: 1;
      min-width: 0;
    }
    .main b {
      font-weight: 500;
    }
    .main small,
    .text {
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .note {
      display: block;
      margin-top: var(--space-lg);
    }
    .field {
      display: grid;
      gap: 6px;
      max-width: 360px;
    }
    .label {
      font-weight: 500;
    }
    .switch {
      position: relative;
      flex: none;
      width: 40px;
      height: 24px;
      padding: 0;
      border: 0;
      border-radius: var(--radius-pill);
      background: var(--field);
      cursor: pointer;
      transition: background-color var(--t-ui) var(--ease-out);
    }
    .switch::after {
      content: '';
      position: absolute;
      top: 3px;
      left: 3px;
      width: 18px;
      height: 18px;
      border-radius: 50%;
      background: var(--surface);
      transition: transform var(--t-ui) var(--ease-out);
    }
    .switch[aria-checked='true'] {
      background: var(--primary);
    }
    .switch[aria-checked='true']::after {
      transform: translateX(16px);
    }
    .switch:focus-visible {
      outline: var(--focus-ring);
      outline-offset: var(--focus-offset);
    }
    .device {
      display: grid;
      gap: var(--space-md);
      justify-items: start;
    }
    .state {
      font-weight: 500;
    }
    .buttons {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-xs);
    }
    .steps {
      display: grid;
      width: 100%;
      counter-reset: step;
    }
    .steps li {
      display: grid;
      grid-template-columns: 24px 36px minmax(0, 1fr);
      align-items: center;
      gap: var(--space-md);
      min-height: 56px;
      padding: var(--space-md) 0;
    }
    .steps li + li {
      border-top: 1px solid var(--rule);
    }
    .steps li::before {
      counter-increment: step;
      content: counter(step);
      display: grid;
      place-items: center;
      width: 24px;
      height: 24px;
      border-radius: 50%;
      background: var(--raised);
      color: var(--muted);
      font-size: var(--fs-small);
      font-weight: 600;
    }
    .pict {
      display: grid;
      place-items: center;
      width: 36px;
      height: 36px;
      border: 1px solid var(--border);
      border-radius: var(--radius-card);
    }
    .pict ardea-notify-icon {
      width: 20px;
      height: 20px;
    }
    .pict.app {
      border: 0;
      background: var(--ground);
    }
    .steps b {
      font-weight: 500;
    }
    .steps small {
      display: block;
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    @media (max-width: 1100px) {
      .col-7,
      .col-5 {
        grid-column: span 12;
      }
      .first {
        order: -1;
      }
    }
  `,
})
export class NotificationSettingsPage implements OnInit {
  private readonly api = inject(NotificationsService);
  private readonly transloco = inject(TranslocoService);
  private readonly session = inject(Session);
  protected readonly push = inject(PushDevice);
  protected readonly organization = injectOrganization().organization;
  private readonly orgId = computed(() => this.organization()?.id);

  protected readonly preferences = getNotificationPreferencesResource(this.orgId);
  protected readonly devices = listPushSubscriptionsResource();

  protected readonly channels = PERSONAL_CHANNELS;
  protected readonly channelLabels = CHANNEL_LABELS;
  protected readonly locales = LOCALES;
  protected readonly saving = signal(false);
  protected readonly saved = signal(false);
  protected readonly problem = signal<string | null>(null);
  protected readonly pushBusy = signal(false);
  protected readonly pushProblem = signal<string | null>(null);
  protected readonly busy = signal<string | null>(null);
  protected readonly deviceNotice = signal<{
    tone: 'success' | 'error';
    key: string;
    params?: Record<string, unknown>;
  } | null>(null);

  protected readonly muted = computed(() =>
    this.preferences.hasValue() ? (this.preferences.value()?.mutedChannels ?? []) : [],
  );
  protected readonly locale = computed(() =>
    this.preferences.hasValue() ? (this.preferences.value()?.locale ?? null) : null,
  );
  protected readonly canMuteCritical = computed(
    () => this.preferences.hasValue() && !!this.preferences.value()?.canMuteCritical,
  );
  protected readonly deviceList = computed(() =>
    this.devices.hasValue() ? (this.devices.value() ?? null) : null,
  );

  ngOnInit(): void {
    void this.push.check().catch(() => undefined);
  }

  protected channelHint(channel: Channel): string {
    if (channel === 'email') return this.session.user()?.email ?? '';
    if (channel === 'push')
      return this.transloco.translate('notify.settings.hints.push', {
        count: this.deviceList()?.length ?? 0,
      });
    return this.transloco.translate(CHANNEL_HINTS[channel] ?? '');
  }

  protected deviceLine(d: PushSubscriptionView): string {
    const date = (v: string) =>
      new Intl.DateTimeFormat(this.transloco.getActiveLang(), { dateStyle: 'medium' }).format(
        new Date(v),
      );
    const parts = [this.transloco.translate('notify.devices.added', { date: date(d.createdAt) })];
    if (d.lastSuccessAt)
      parts.push(
        this.transloco.translate('notify.devices.lastMessage', { date: date(d.lastSuccessAt) }),
      );
    if (num(d.failedCount) > 0)
      parts.push(this.transloco.translate('notify.devices.failed', { count: num(d.failedCount) }));
    return parts.join(' · ');
  }

  protected toggle(channel: Channel): void {
    const muted = this.muted();
    void this.savePreferences(
      muted.includes(channel) ? muted.filter((c) => c !== channel) : [...muted, channel],
      this.locale(),
    );
  }

  protected setLocale(value: string): void {
    void this.savePreferences([...this.muted()], value || null);
  }

  protected async enable(): Promise<void> {
    await this.runPush(() => this.push.enable());
  }

  protected async disable(): Promise<void> {
    await this.runPush(() => this.push.disable());
  }

  protected async test(device: PushSubscriptionView): Promise<void> {
    this.busy.set('test:' + device.id);
    this.deviceNotice.set(null);
    try {
      const result = await firstValueFrom(this.api.testPushSubscription(device.id));
      this.deviceNotice.set(
        result.ok
          ? { tone: 'success', key: 'notify.devices.testOk', params: { name: device.label } }
          : result.removed
            ? { tone: 'error', key: 'notify.devices.testGone', params: { name: device.label } }
            : {
                tone: 'error',
                key: 'notify.devices.testFailed',
                params: { name: device.label, status: result.status ?? '' },
              },
      );
      this.devices.reload();
    } catch (error) {
      this.deviceNotice.set({
        tone: 'error',
        key: problemMessageKey(error, { fallback: 'notify.saveFailed' }),
      });
    } finally {
      this.busy.set(null);
    }
  }

  protected async remove(device: PushSubscriptionView): Promise<void> {
    this.busy.set('remove:' + device.id);
    this.deviceNotice.set(null);
    try {
      await firstValueFrom(this.api.deletePushSubscription(device.id));
      this.deviceNotice.set({
        tone: 'success',
        key: 'notify.devices.removed',
        params: { name: device.label },
      });
      this.devices.reload();
      await this.push.check();
    } catch (error) {
      this.deviceNotice.set({
        tone: 'error',
        key: problemMessageKey(error, { fallback: 'notify.saveFailed' }),
      });
    } finally {
      this.busy.set(null);
    }
  }

  private async runPush(work: () => Promise<unknown>): Promise<void> {
    this.pushBusy.set(true);
    this.pushProblem.set(null);
    try {
      await work();
      this.devices.reload();
    } catch (error) {
      this.pushProblem.set(problemMessageKey(error, { fallback: 'notify.push.failed' }));
      await this.push.check().catch(() => undefined);
    } finally {
      this.pushBusy.set(false);
    }
  }

  private async savePreferences(mutedChannels: string[], locale: string | null): Promise<void> {
    const org = this.orgId();
    if (!org) return;
    this.saving.set(true);
    this.saved.set(false);
    this.problem.set(null);
    try {
      const result = await firstValueFrom(
        this.api.setNotificationPreferences(org, { mutedChannels, locale }),
      );
      this.preferences.set(result);
      this.saved.set(true);
    } catch (error) {
      this.problem.set(problemMessageKey(error, { fallback: 'notify.saveFailed' }));
    } finally {
      this.saving.set(false);
    }
  }
}
