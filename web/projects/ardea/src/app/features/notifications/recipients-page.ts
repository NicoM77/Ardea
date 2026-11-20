import { Component, computed, inject, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import {
  UiButton,
  UiCard,
  UiCopyButton,
  UiInput,
  UiLoader,
  UiMenu,
  UiMenuItem,
  UiNotice,
  UiOverflowMenu,
  UiPill,
} from 'ui';
import { firstValueFrom } from 'rxjs';
import {
  listExternalAddressesResource,
  listWebhookEndpointsResource,
  NotificationsService,
  type ExternalAddressView,
  type WebhookEndpointView,
} from '../../core/api/client';
import { problemMessageKey } from '../../core/api/problem';
import { injectOrganization } from '../organizations/current-organization';
import { canManage } from '../organizations/org-format';
import { NotifyFrame } from './notify-frame';
import { ADDRESS_STATE_LABELS } from './notify-format';
import { NotifyIcon } from './notify-icon';

const ENDPOINT_PROBLEMS = {
  invalid_endpoint: 'notify.webhooks.errors.invalid',
  endpoint_in_use: 'notify.webhooks.errors.inUse',
};
const ADDRESS_PROBLEMS = {
  invalid_address: 'notify.addresses.errors.invalid',
  address_exists: 'notify.addresses.errors.exists',
  address_limit: 'notify.addresses.errors.limit',
  address_confirmed: 'notify.addresses.errors.confirmed',
  confirmation_limit: 'notify.addresses.errors.confirmationLimit',
  smtp_not_configured: 'notify.addresses.errors.smtp',
  address_in_use: 'notify.addresses.errors.inUse',
};

@Component({
  selector: 'ardea-recipients-page',
  imports: [
    TranslocoPipe,
    NotifyIcon,
    UiButton,
    UiCard,
    UiCopyButton,
    UiInput,
    UiLoader,
    UiMenu,
    UiMenuItem,
    UiNotice,
    UiOverflowMenu,
    UiPill,
    NotifyFrame,
  ],
  template: `
    @if (organization(); as org) {
      <ardea-notify-frame [organization]="org" current="recipients">
        <div class="grid">
          <ui-card class="span-7" [heading]="'notify.webhooks.title' | transloco">
            <span uiCardMeta>{{ 'notify.webhooks.meta' | transloco }}</span>
            <div class="live" role="status">
              @if (hookNotice(); as n) {
                <ui-notice [tone]="n.tone">{{ n.key | transloco: n.params }}</ui-notice>
              }
            </div>
            @if (secret(); as s) {
              <ui-notice class="secret-box" [heading]="'notify.webhooks.secretOnce' | transloco">
                <span class="secret"
                  ><code>{{ s }}</code
                  ><ui-copy-button [text]="s" [label]="'notify.webhooks.copySecret' | transloco"
                /></span>
              </ui-notice>
            }
            @if (endpointList(); as list) {
              @if (list.length) {
                <ul class="rows" [attr.aria-label]="'notify.webhooks.title' | transloco">
                  @for (e of list; track e.id) {
                    <li>
                      <ardea-notify-icon class="row-icon" name="webhook" />
                      <span class="main">
                        <b>{{ e.name }}</b>
                        <small
                          ><code>{{ e.host }}</code>
                          @if (!e.enabled) {
                            · {{ 'notify.webhooks.off' | transloco }}
                          }
                        </small>
                      </span>
                      <span class="end">
                        @if (e.lastStatus) {
                          {{ e.lastStatus }}<br />{{ when(e.lastStatusAt) }}
                        } @else {
                          {{ 'notify.webhooks.neverCalled' | transloco }}
                        }
                      </span>
                      <span class="buttons">
                        <button
                          uiButton
                          type="button"
                          [loading]="busy() === 'test:' + e.id"
                          (click)="test(e)"
                        >
                          {{ 'notify.webhooks.test' | transloco }}
                        </button>
                        <ui-overflow-menu
                          [name]="e.name"
                          [busy]="busy() === 'rotate:' + e.id || busy() === 'delete:' + e.id"
                        >
                          <ng-template>
                            <div uiMenu>
                              <button uiMenuItem type="button" (triggered)="rotate(e)">
                                {{ 'notify.webhooks.rotate' | transloco }}
                              </button>
                              <button
                                uiMenuItem
                                destructive
                                type="button"
                                (triggered)="deleteEndpoint(e)"
                              >
                                {{ 'notify.webhooks.delete' | transloco }}
                              </button>
                            </div>
                          </ng-template>
                        </ui-overflow-menu>
                      </span>
                    </li>
                  }
                </ul>
              } @else {
                <p class="muted">{{ 'notify.webhooks.none' | transloco }}</p>
              }
            } @else if (endpoints.error()) {
              <ui-notice tone="error">{{ 'notify.loadFailed' | transloco }}</ui-notice>
            } @else {
              <ui-loader [label]="'notify.loading' | transloco" />
            }
            <form
              class="add"
              novalidate
              (submit)="addEndpoint($event, hookName.value, hookUrl.value)"
            >
              <div class="field">
                <label class="label" for="hook-name">{{
                  'notify.webhooks.name' | transloco
                }}</label>
                <input uiInput id="hook-name" type="text" maxlength="200" #hookName />
              </div>
              <div class="field">
                <label class="label" for="hook-url">{{ 'notify.webhooks.url' | transloco }}</label>
                <input
                  uiInput
                  id="hook-url"
                  type="url"
                  inputmode="url"
                  spellcheck="false"
                  placeholder="https://hooks.example.org/ardea"
                  #hookUrl
                />
              </div>
              <button uiButton type="submit" [loading]="busy() === 'add-hook'">
                {{ 'notify.webhooks.add' | transloco }}
              </button>
            </form>
          </ui-card>

          <ui-card class="span-5" [heading]="'notify.addresses.title' | transloco">
            <span uiCardMeta>{{
              'notify.addresses.meta' | transloco: { count: addressList()?.length ?? 0 }
            }}</span>
            <div class="live" role="status">
              @if (addressNotice(); as n) {
                <ui-notice [tone]="n.tone">{{ n.key | transloco: n.params }}</ui-notice>
              }
            </div>
            @if (addressList(); as list) {
              @if (list.length) {
                <ul class="rows" [attr.aria-label]="'notify.addresses.title' | transloco">
                  @for (a of list; track a.id) {
                    <li>
                      <span class="main">
                        <b>{{ a.address }}</b>
                        <small>{{ addressLine(a) }}</small>
                      </span>
                      <ui-pill [tone]="a.state === 'confirmed' ? 'done' : 'neutral'">{{
                        stateLabels[a.state] ?? a.state | transloco
                      }}</ui-pill>
                      @if (a.state !== 'unsubscribed') {
                        <ui-overflow-menu
                          [name]="a.address"
                          [busy]="busy() === 'resend:' + a.id || busy() === 'remove:' + a.id"
                        >
                          <ng-template>
                            <div uiMenu>
                              @if (resendable(a)) {
                                <button uiMenuItem type="button" (triggered)="resend(a)">
                                  {{ 'notify.addresses.resend' | transloco }}
                                </button>
                              }
                              <button
                                uiMenuItem
                                destructive
                                type="button"
                                (triggered)="removeAddress(a)"
                              >
                                {{ 'notify.addresses.remove' | transloco }}
                              </button>
                            </div>
                          </ng-template>
                        </ui-overflow-menu>
                      }
                    </li>
                  }
                </ul>
              } @else {
                <p class="muted">{{ 'notify.addresses.none' | transloco }}</p>
              }
            } @else if (addresses.error()) {
              <ui-notice tone="error">{{ 'notify.loadFailed' | transloco }}</ui-notice>
            } @else {
              <ui-loader [label]="'notify.loading' | transloco" />
            }
            <form class="add two" novalidate (submit)="addAddress($event, newAddress.value)">
              <div class="field">
                <label class="label" for="new-address">{{
                  'notify.addresses.address' | transloco
                }}</label>
                <input
                  uiInput
                  id="new-address"
                  type="email"
                  autocomplete="off"
                  spellcheck="false"
                  placeholder="name@example.org"
                  #newAddress
                />
              </div>
              <button uiButton type="submit" [loading]="busy() === 'add-address'">
                {{ 'notify.addresses.add' | transloco }}
              </button>
            </form>
            <p class="foot">{{ 'notify.addresses.foot' | transloco }}</p>
          </ui-card>
        </div>
      </ardea-notify-frame>
    }
  `,
  styles: `
    :host {
      display: block;
    }
    .grid {
      display: grid;
      grid-template-columns: repeat(12, minmax(0, 1fr));
      gap: var(--gap);
      align-items: start;
    }
    .span-7 {
      grid-column: span 7;
    }
    .span-5 {
      grid-column: span 5;
    }
    .live:empty {
      display: none;
    }
    .live,
    .secret-box {
      display: block;
      margin-bottom: var(--space-md);
    }
    .secret {
      display: flex;
      align-items: center;
      gap: var(--space-sm);
      margin-top: var(--space-sm);
    }
    .secret code {
      flex: 1;
      min-width: 0;
      padding: var(--space-sm);
      border-radius: var(--radius-control);
      background: var(--surface);
      font: 13px/1.4 var(--mono);
      overflow-wrap: anywhere;
    }
    .rows {
      display: grid;
    }
    .rows li {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-sm) var(--space-md);
      min-height: 56px;
      padding: var(--space-sm) 0;
    }
    .rows li + li {
      border-top: 1px solid var(--rule);
    }
    .row-icon {
      width: 16px;
      height: 16px;
      color: var(--muted);
    }
    .main {
      display: grid;
      flex: 1;
      min-width: 160px;
    }
    .main b {
      font-weight: 500;
      overflow-wrap: anywhere;
    }
    .main small,
    .end,
    .muted,
    .foot {
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .main code {
      font: 13px/1.4 var(--mono);
    }
    .end {
      text-align: right;
    }
    .buttons {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-xs);
    }
    .add {
      display: grid;
      grid-template-columns: minmax(0, 1fr) minmax(0, 1.6fr) auto;
      align-items: end;
      gap: var(--space-md);
      margin-top: var(--space-lg);
      padding-top: var(--space-lg);
      border-top: 1px solid var(--rule);
    }
    .add.two {
      grid-template-columns: minmax(0, 1fr) auto;
    }
    .field {
      display: grid;
      gap: 6px;
      min-width: 0;
    }
    .label {
      font-weight: 500;
    }
    .foot {
      margin-top: var(--space-md);
      font-size: var(--fs-small);
      line-height: var(--lh-small);
      color: var(--faint);
    }
    @media (max-width: 1100px) {
      .span-7,
      .span-5 {
        grid-column: span 12;
      }
    }
    @media (max-width: 640px) {
      .add,
      .add.two {
        grid-template-columns: minmax(0, 1fr);
      }
      .end {
        text-align: left;
      }
    }
  `,
})
export class RecipientsPage {
  private readonly api = inject(NotificationsService);
  private readonly transloco = inject(TranslocoService);
  protected readonly organization = injectOrganization().organization;
  private readonly orgId = computed(() => {
    const org = this.organization();
    return org && canManage(org.role) ? org.id : undefined;
  });

  protected readonly endpoints = listWebhookEndpointsResource(this.orgId);
  protected readonly addresses = listExternalAddressesResource(this.orgId);
  protected readonly stateLabels = ADDRESS_STATE_LABELS;
  protected readonly busy = signal<string | null>(null);
  protected readonly secret = signal<string | null>(null);
  protected readonly hookNotice = signal<{
    tone: 'success' | 'error';
    key: string;
    params?: Record<string, unknown>;
  } | null>(null);
  protected readonly addressNotice = signal<{
    tone: 'success' | 'error';
    key: string;
    params?: Record<string, unknown>;
  } | null>(null);

  protected readonly endpointList = computed(() =>
    this.endpoints.hasValue() ? (this.endpoints.value() ?? null) : null,
  );
  protected readonly addressList = computed(() =>
    this.addresses.hasValue() ? (this.addresses.value() ?? null) : null,
  );

  protected when(value: string | null | undefined): string {
    return value
      ? new Intl.DateTimeFormat(this.transloco.getActiveLang(), {
          dateStyle: 'medium',
          timeStyle: 'short',
        }).format(new Date(value))
      : '';
  }

  protected resendable(a: ExternalAddressView): boolean {
    return a.state === 'pending' || a.state === 'expired';
  }

  protected addressLine(a: ExternalAddressView): string {
    return a.confirmedAt
      ? this.transloco.translate('notify.addresses.confirmedAt', { date: this.when(a.confirmedAt) })
      : this.transloco.translate('notify.addresses.addedAt', { date: this.when(a.createdAt) });
  }

  protected async addEndpoint(event: Event, name: string, url: string): Promise<void> {
    event.preventDefault();
    const org = this.organization();
    if (!org) return;
    if (!name.trim() || !url.trim()) {
      this.hookNotice.set({ tone: 'error', key: 'notify.webhooks.errors.required' });
      return;
    }
    await this.run('add-hook', this.hookNotice, ENDPOINT_PROBLEMS, async () => {
      const created = await firstValueFrom(
        this.api.createWebhookEndpoint(org.id, {
          name: name.trim(),
          url: url.trim(),
          enabled: true,
          rotateSecret: null,
        }),
      );
      this.secret.set(created.secret ?? null);
      (event.target as HTMLFormElement).reset();
      this.endpoints.reload();
      return { tone: 'success', key: 'notify.webhooks.added', params: { name: created.name } };
    });
  }

  protected async test(endpoint: WebhookEndpointView): Promise<void> {
    const org = this.organization();
    if (!org) return;
    await this.run('test:' + endpoint.id, this.hookNotice, ENDPOINT_PROBLEMS, async () => {
      const result = await firstValueFrom(this.api.testWebhookEndpoint(org.id, endpoint.id));
      this.endpoints.reload();
      return result.ok
        ? {
            tone: 'success',
            key: 'notify.webhooks.testOk',
            params: { name: endpoint.name, status: result.status ?? '' },
          }
        : {
            tone: 'error',
            key: 'notify.webhooks.testFailed',
            params: { name: endpoint.name, status: result.status ?? result.error ?? '' },
          };
    });
  }

  protected async rotate(endpoint: WebhookEndpointView): Promise<void> {
    const org = this.organization();
    if (!org) return;
    await this.run('rotate:' + endpoint.id, this.hookNotice, ENDPOINT_PROBLEMS, async () => {
      const changed = await firstValueFrom(
        this.api.updateWebhookEndpoint(org.id, endpoint.id, {
          name: null,
          url: null,
          enabled: null,
          rotateSecret: true,
        }),
      );
      this.secret.set(changed.secret ?? null);
      return { tone: 'success', key: 'notify.webhooks.rotated', params: { name: endpoint.name } };
    });
  }

  protected async deleteEndpoint(endpoint: WebhookEndpointView): Promise<void> {
    const org = this.organization();
    if (!org) return;
    await this.run('delete:' + endpoint.id, this.hookNotice, ENDPOINT_PROBLEMS, async () => {
      await firstValueFrom(this.api.deleteWebhookEndpoint(org.id, endpoint.id));
      this.endpoints.reload();
      return { tone: 'success', key: 'notify.webhooks.deleted', params: { name: endpoint.name } };
    });
  }

  protected async addAddress(event: Event, address: string): Promise<void> {
    event.preventDefault();
    const org = this.organization();
    if (!org) return;
    await this.run('add-address', this.addressNotice, ADDRESS_PROBLEMS, async () => {
      const added = await firstValueFrom(
        this.api.addExternalAddress(org.id, { address: address.trim() }),
      );
      (event.target as HTMLFormElement).reset();
      this.addresses.reload();
      return { tone: 'success', key: 'notify.addresses.added', params: { address: added.address } };
    });
  }

  protected async resend(address: ExternalAddressView): Promise<void> {
    const org = this.organization();
    if (!org) return;
    await this.run('resend:' + address.id, this.addressNotice, ADDRESS_PROBLEMS, async () => {
      await firstValueFrom(this.api.resendAddressConfirmation(org.id, address.id));
      this.addresses.reload();
      return {
        tone: 'success',
        key: 'notify.addresses.resent',
        params: { address: address.address },
      };
    });
  }

  protected async removeAddress(address: ExternalAddressView): Promise<void> {
    const org = this.organization();
    if (!org) return;
    await this.run('remove:' + address.id, this.addressNotice, ADDRESS_PROBLEMS, async () => {
      await firstValueFrom(this.api.deleteExternalAddress(org.id, address.id));
      this.addresses.reload();
      return {
        tone: 'success',
        key: 'notify.addresses.removed',
        params: { address: address.address },
      };
    });
  }

  private async run(
    id: string,
    notice: ReturnType<
      typeof signal<{
        tone: 'success' | 'error';
        key: string;
        params?: Record<string, unknown>;
      } | null>
    >,
    codes: Record<string, string>,
    work: () => Promise<{
      tone: 'success' | 'error';
      key: string;
      params?: Record<string, unknown>;
    }>,
  ): Promise<void> {
    this.busy.set(id);
    notice.set(null);
    try {
      notice.set(await work());
    } catch (error) {
      notice.set({
        tone: 'error',
        key: problemMessageKey(error, { codes, fallback: 'notify.saveFailed' }),
      });
    } finally {
      this.busy.set(null);
    }
  }
}
