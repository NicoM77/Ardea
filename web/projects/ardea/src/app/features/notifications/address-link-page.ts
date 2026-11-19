import { Component, computed, inject, input, linkedSignal, signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { UiButton, UiLoader, UiNotice } from 'ui';
import { firstValueFrom, type Observable } from 'rxjs';
import { NotificationsService, previewAddressLinkResource } from '../../core/api/client';
import { problemMessageKey } from '../../core/api/problem';
import { Session } from '../../core/session/session';

export type LinkMode = 'confirm' | 'unsubscribe' | 'resubscribe';

/** What the page shows: the question, the outcome, or why the link does not work. */
type View = 'ask' | 'confirmed' | 'unsubscribed' | 'resubscribed' | 'invalid';

/**
 * The pages behind the links in e-mails to external addresses (master plan 8.3, 8.5; ADR 038 decision 9;
 * task 5.8; mockup notification-link.html): confirm an address, unsubscribe it, or take the unsubscribe back.
 * They work without signing in; the token in the link is the proof. Opening a link only shows what it does: the
 * change is a button, so a mail scanner that follows links changes nothing.
 */
@Component({
  selector: 'ardea-address-link-page',
  imports: [TranslocoPipe, UiButton, UiLoader, UiNotice],
  template: `
    <h1>{{ heading() | transloco }}</h1>
    <div class="auth-form" [attr.aria-busy]="preview.isLoading() || busy()">
      @if (!token()) {
        <ui-notice [heading]="'notify.link.invalid.heading' | transloco">{{
          'notify.link.invalid.text' | transloco
        }}</ui-notice>
      } @else if (preview.hasValue() && preview.value(); as p) {
        @switch (view()) {
          @case ('ask') {
            <p class="lead">
              {{ question() | transloco: { organization: p.organizationName, address: p.address } }}
            </p>
            @if (problem()) {
              <ui-notice tone="error">{{ problem() | transloco }}</ui-notice>
            }
            <button
              uiButton
              variant="primary"
              block
              type="button"
              [loading]="busy()"
              (click)="act(mode())"
            >
              {{ action() | transloco }}
            </button>
            @if (mode() === 'confirm') {
              <p class="auth-foot">{{ 'notify.link.confirm.ignore' | transloco }}</p>
            }
          }
          @case ('confirmed') {
            <ui-notice
              tone="success"
              [heading]="
                'notify.link.confirmed.heading' | transloco: { organization: p.organizationName }
              "
            >
              {{ 'notify.link.confirmed.text' | transloco }}
            </ui-notice>
          }
          @case ('unsubscribed') {
            <ui-notice
              tone="success"
              [heading]="'notify.link.unsubscribed.heading' | transloco: { address: p.address }"
            >
              {{
                'notify.link.unsubscribed.text' | transloco: { organization: p.organizationName }
              }}
            </ui-notice>
            @if (problem()) {
              <ui-notice tone="error">{{ problem() | transloco }}</ui-notice>
            }
            <button uiButton block type="button" [loading]="busy()" (click)="act('resubscribe')">
              {{ 'notify.link.resubscribe.action' | transloco }}
            </button>
          }
          @case ('resubscribed') {
            <ui-notice
              tone="success"
              [heading]="
                'notify.link.resubscribed.heading'
                  | transloco: { organization: p.organizationName, address: p.address }
              "
            >
              {{ 'notify.link.resubscribed.text' | transloco }}
            </ui-notice>
          }
          @default {
            <ui-notice [heading]="'notify.link.invalid.heading' | transloco">
              {{ 'notify.link.invalid.text' | transloco }}
            </ui-notice>
          }
        }
      } @else if (preview.error()) {
        <ui-notice [heading]="'notify.link.invalid.heading' | transloco">{{
          'notify.link.invalid.text' | transloco
        }}</ui-notice>
      } @else {
        <ui-loader class="wait" size="inline" [label]="'notify.loading' | transloco" />
      }
    </div>
  `,
  styles: `
    .wait {
      justify-self: start;
      min-height: 24px;
    }
  `,
})
export class AddressLinkPage {
  /** From the route's data. */
  readonly mode = input.required<LinkMode>();
  /** From the query string. */
  readonly token = input<string | null>(null);

  private readonly api = inject(NotificationsService);
  private readonly session = inject(Session);

  protected readonly preview = previewAddressLinkResource(
    computed(() => (this.token() ? { token: this.token()! } : undefined)),
  );
  protected readonly busy = signal(false);
  protected readonly problem = signal<string | null>(null);

  /** Starts from the address's state, then follows what the person did here. */
  protected readonly view = linkedSignal<View>(() => {
    const state = this.preview.hasValue() ? this.preview.value()?.state : undefined;
    switch (this.mode()) {
      case 'confirm':
        return state === 'confirmed' ? 'confirmed' : state === 'pending' ? 'ask' : 'invalid';
      case 'unsubscribe':
        return state === 'unsubscribed'
          ? 'unsubscribed'
          : state === 'confirmed'
            ? 'ask'
            : 'invalid';
      default:
        return state === 'unsubscribed'
          ? 'ask'
          : state === 'confirmed'
            ? 'resubscribed'
            : 'invalid';
    }
  });

  protected readonly heading = computed(() => {
    switch (this.view()) {
      case 'confirmed':
        return 'notify.link.confirmed.title';
      case 'unsubscribed':
        return 'notify.link.unsubscribed.title';
      case 'resubscribed':
        return 'notify.link.resubscribed.title';
      case 'invalid':
        return 'notify.link.invalid.title';
      default:
        return this.mode() === 'confirm'
          ? 'notify.link.confirm.title'
          : this.mode() === 'unsubscribe'
            ? 'notify.link.unsubscribe.title'
            : 'notify.link.resubscribe.title';
    }
  });
  protected readonly question = computed(() =>
    this.mode() === 'confirm'
      ? 'notify.link.confirm.question'
      : this.mode() === 'unsubscribe'
        ? 'notify.link.unsubscribe.question'
        : 'notify.link.resubscribe.question',
  );
  protected readonly action = computed(() =>
    this.mode() === 'confirm'
      ? 'notify.link.confirm.action'
      : this.mode() === 'unsubscribe'
        ? 'notify.link.unsubscribe.action'
        : 'notify.link.resubscribe.action',
  );

  protected async act(mode: LinkMode): Promise<void> {
    const token = this.token();
    if (!token) return;
    this.busy.set(true);
    this.problem.set(null);
    const calls: Record<LinkMode, () => Observable<unknown>> = {
      confirm: () => this.api.confirmExternalAddress({ token }),
      unsubscribe: () => this.api.unsubscribeExternalAddress({ token }),
      resubscribe: () => this.api.resubscribeExternalAddress({ token }),
    };
    try {
      // The session read sets the antiforgery cookie the request carries; the token is what proves it.
      await this.session.ready();
      await firstValueFrom(calls[mode]());
      this.view.set(
        mode === 'confirm' ? 'confirmed' : mode === 'unsubscribe' ? 'unsubscribed' : 'resubscribed',
      );
    } catch (error) {
      this.problem.set(
        problemMessageKey(error, {
          statuses: { 404: 'notify.link.invalid.text' },
          fallback: 'notify.link.failed',
        }),
      );
    } finally {
      this.busy.set(false);
    }
  }
}
