import { Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiLoader, UiNotice } from 'ui';
import { AuthenticationService } from '../../core/api/client';
import { problemMessageKey } from '../../core/api/problem';
import { Session } from '../../core/session/session';

type State = 'confirming' | 'confirmed' | 'failed' | 'incomplete';

@Component({
  selector: 'ardea-confirm-email-page',
  imports: [RouterLink, TranslocoPipe, UiButton, UiLoader, UiNotice],
  template: `
    <h1>{{ 'auth.confirmEmail.title' | transloco }}</h1>

    <div class="auth-form" [attr.aria-busy]="state() === 'confirming'">
      @switch (state()) {
        @case ('confirming') {
          <ui-loader
            class="wait"
            size="inline"
            [label]="'auth.confirmEmail.confirming' | transloco"
          />
        }
        @case ('confirmed') {
          <ui-notice tone="success" [heading]="'auth.confirmEmail.done.heading' | transloco">
            {{ 'auth.confirmEmail.done.text' | transloco }}
          </ui-notice>
          <a uiButton variant="primary" block routerLink="/">
            {{ (signedIn() ? 'auth.confirmEmail.continue' : 'auth.signIn.title') | transloco }}
          </a>
        }
        @case ('incomplete') {
          <ui-notice tone="error" [heading]="'auth.confirmEmail.incomplete.heading' | transloco">
            {{ 'auth.confirmEmail.incomplete.text' | transloco }}
          </ui-notice>
        }
        @case ('failed') {
          <ui-notice tone="error">{{ problem() | transloco }}</ui-notice>
          @if (canRetry()) {
            <button uiButton block type="button" (click)="confirm()">
              {{ 'auth.confirmEmail.retry' | transloco }}
            </button>
          }
        }
      }
    </div>

    <p class="auth-foot">
      <a class="link" routerLink="/">{{ 'auth.confirmEmail.home' | transloco }}</a>
    </p>
  `,
  styles: `
    .wait {
      justify-self: start;
      min-height: 24px;
    }
  `,
})
export class ConfirmEmailPage implements OnInit {
  readonly userId = input<string | null>(null);
  readonly token = input<string | null>(null);

  private readonly auth = inject(AuthenticationService);
  private readonly session = inject(Session);

  protected readonly state = signal<State>('confirming');
  protected readonly problem = signal<string | null>(null);
  protected readonly signedIn = computed(() => this.session.state() === 'signedIn');
  protected readonly canRetry = computed(() => this.problem() !== 'auth.confirmEmail.invalid');

  ngOnInit(): void {
    void this.confirm();
  }

  protected async confirm(): Promise<void> {
    const userId = this.userId();
    const token = this.token();
    if (!userId || !token) {
      this.state.set('incomplete');
      return;
    }
    this.state.set('confirming');
    try {
      await this.session.ready();
      await firstValueFrom(this.auth.confirmEmail({ userId, token }));
      this.state.set('confirmed');
    } catch (error) {
      this.problem.set(
        problemMessageKey(error, {
          codes: { invalid_token: 'auth.confirmEmail.invalid' },
          statuses: { 400: 'auth.confirmEmail.invalid' },
          fallback: 'auth.confirmEmail.invalid',
        }),
      );
      this.state.set('failed');
    }
  }
}
