import { Component, computed, inject, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { UiButton, UiInput, UiLoader, UiNotice, UiStatus } from 'ui';
import { listDeliveriesResource, type DeliveryView } from '../../core/api/client';
import { reloadOnLiveChange } from '../../core/live/live-events';
import { num } from '../actions/action-format';
import { injectOrganization } from '../organizations/current-organization';
import { canManage } from '../organizations/org-format';
import { NotifyFrame } from './notify-frame';
import {
  CHANNEL_LABELS,
  CHANNELS,
  DELIVERY_STATE_LABELS,
  DELIVERY_STATES,
  deliveryStatus,
  logSummary,
} from './notify-format';

const PAGE = 50;

/**
 * The delivery log (master plan 8.6, 11.4; task 5.8; mockup notification-rules.html, «Delivery log»): one row per
 * message, newest first, with its state, attempts and why it failed; filtered by state and channel.
 */
@Component({
  selector: 'ardea-delivery-log-page',
  imports: [TranslocoPipe, UiButton, UiInput, UiLoader, UiNotice, UiStatus, NotifyFrame],
  template: `
    @if (organization(); as org) {
      <ardea-notify-frame [organization]="org" current="log">
        <div class="filters" role="group" [attr.aria-label]="'notify.log.filters' | transloco">
          <div class="field">
            <label class="label" for="log-state">{{ 'notify.log.state' | transloco }}</label>
            <select uiInput id="log-state" (change)="setState($any($event.target).value)">
              <option value="">{{ 'notify.log.allStates' | transloco }}</option>
              @for (s of states; track s) {
                <option [value]="s" [selected]="state() === s">
                  {{ stateLabels[s] | transloco }}
                </option>
              }
            </select>
          </div>
          <div class="field">
            <label class="label" for="log-channel">{{ 'notify.log.channel' | transloco }}</label>
            <select uiInput id="log-channel" (change)="setChannel($any($event.target).value)">
              <option value="">{{ 'notify.log.allChannels' | transloco }}</option>
              @for (c of channels; track c) {
                <option [value]="c" [selected]="channel() === c">
                  {{ channelLabels[c] | transloco }}
                </option>
              }
            </select>
          </div>
        </div>

        @if (rows(); as list) {
          <p class="summary" role="status">
            {{
              'notify.log.summary'
                | transloco: { count: list.length, failed: summary().failed, dead: summary().dead }
            }}
          </p>
          @if (list.length) {
            <table class="log">
              <caption class="sr-only">
                {{
                  'notify.tabs.log' | transloco
                }}
              </caption>
              <thead>
                <tr>
                  <th scope="col">{{ 'notify.log.when' | transloco }}</th>
                  <th scope="col">{{ 'notify.log.recipient' | transloco }}</th>
                  <th scope="col">{{ 'notify.log.channel' | transloco }}</th>
                  <th scope="col">{{ 'notify.log.message' | transloco }}</th>
                  <th scope="col">{{ 'notify.log.state' | transloco }}</th>
                  <th scope="col">{{ 'notify.log.detail' | transloco }}</th>
                </tr>
              </thead>
              <tbody>
                @for (d of list; track d.id) {
                  <tr>
                    <td>{{ when(d.createdAt) }}</td>
                    <td>{{ d.recipient.label || ('notify.recipients.gone' | transloco) }}</td>
                    <td>{{ channelLabels[d.channel] ?? d.channel | transloco }}</td>
                    <td>
                      @if (d.kind === 'digest') {
                        {{ 'notify.mode.digest' | transloco }}
                        <small>{{ when(d.periodStart) }} – {{ when(d.periodEnd) }}</small>
                      } @else {
                        {{ 'notify.mode.immediate' | transloco }}
                        <small>{{
                          'notify.log.events' | transloco: { count: num(d.events) }
                        }}</small>
                      }
                    </td>
                    <td>
                      <ui-status [kind]="statusOf(d)" [word]="stateLabels[d.state] ?? null" />
                      @if (d.state === 'failed') {
                        <small>{{
                          'notify.log.attempt' | transloco: { count: num(d.attempts) }
                        }}</small>
                      }
                    </td>
                    <td class="detail">
                      <small>{{ d.lastError }}</small>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
            @if (hasMore()) {
              <div class="more">
                <button uiButton type="button" [loading]="log.isLoading()" (click)="more()">
                  {{ 'notify.log.more' | transloco }}
                </button>
              </div>
            }
          } @else {
            <p class="muted">{{ 'notify.log.none' | transloco }}</p>
          }
        } @else if (log.error()) {
          <ui-notice tone="error">{{ 'notify.loadFailed' | transloco }}</ui-notice>
        } @else {
          <ui-loader [label]="'notify.loading' | transloco" />
        }
      </ardea-notify-frame>
    }
  `,
  styles: `
    :host {
      display: block;
    }
    .filters {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-md) var(--space-lg);
      margin-bottom: var(--space-lg);
    }
    .field {
      display: grid;
      gap: 6px;
      min-width: 200px;
    }
    .label {
      font-weight: 500;
    }
    .summary,
    .muted {
      margin-bottom: var(--space-md);
      color: var(--muted);
    }
    .log {
      width: 100%;
      border-collapse: collapse;
    }
    th {
      height: 32px;
      padding-right: var(--space-md);
      color: var(--muted);
      font-size: var(--fs-small);
      font-weight: 400;
      text-align: left;
      white-space: nowrap;
    }
    td {
      padding: var(--space-sm) var(--space-md) var(--space-sm) 0;
      border-top: 1px solid var(--rule);
      vertical-align: top;
    }
    td small {
      display: block;
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .detail {
      max-width: 360px;
      overflow-wrap: anywhere;
    }
    .more {
      display: flex;
      justify-content: center;
      margin-top: var(--space-lg);
    }
    @media (max-width: 640px) {
      thead {
        display: none;
      }
      tr {
        display: grid;
        grid-template-columns: minmax(0, 1fr) auto;
        gap: 2px var(--space-md);
        padding: var(--space-sm) 0;
        border-top: 1px solid var(--rule);
      }
      td {
        padding: 0;
        border: 0;
      }
      td.detail {
        grid-column: 1 / -1;
      }
    }
  `,
})
export class DeliveryLogPage {
  private readonly transloco = inject(TranslocoService);
  protected readonly organization = injectOrganization().organization;
  private readonly orgId = computed(() => {
    const org = this.organization();
    return org && canManage(org.role) ? org.id : undefined;
  });

  protected readonly states = DELIVERY_STATES;
  protected readonly stateLabels = DELIVERY_STATE_LABELS;
  protected readonly channels = CHANNELS;
  protected readonly channelLabels = CHANNEL_LABELS;
  protected readonly num = num;

  protected readonly state = signal('');
  protected readonly channel = signal('');
  private readonly limit = signal(PAGE);

  protected readonly log = listDeliveriesResource(
    this.orgId,
    computed(() => ({
      state: this.state() || undefined,
      channel: this.channel() || undefined,
      limit: this.limit(),
    })),
  );

  protected readonly rows = computed(() =>
    this.log.hasValue() ? (this.log.value()?.items ?? null) : null,
  );
  protected readonly hasMore = computed(
    () => !!(this.log.hasValue() && this.log.value()?.nextCursor) && this.limit() < 200,
  );
  protected readonly summary = computed(() => logSummary(this.rows() ?? []));

  constructor() {
    reloadOnLiveChange(this.log);
  }

  protected setState(value: string): void {
    this.limit.set(PAGE);
    this.state.set(value);
  }

  protected setChannel(value: string): void {
    this.limit.set(PAGE);
    this.channel.set(value);
  }

  protected more(): void {
    this.limit.update((n) => Math.min(n + PAGE, 200));
  }

  protected statusOf(d: DeliveryView) {
    return deliveryStatus(d.state);
  }

  protected when(value: string | null | undefined): string {
    return value
      ? new Intl.DateTimeFormat(this.transloco.getActiveLang(), {
          dateStyle: 'medium',
          timeStyle: 'short',
        }).format(new Date(value))
      : '';
  }
}
