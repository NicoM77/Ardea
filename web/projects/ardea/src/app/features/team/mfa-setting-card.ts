import { Component, computed, inject, input, linkedSignal, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiCard, UiNotice } from 'ui';
import { OrganizationsService, type SessionOrganization } from '../../core/api/client';
import { problemMessageKey, problemOf } from '../../core/api/problem';
import { Session } from '../../core/session/session';
import { TEAM_PROBLEMS } from './team-problems';

/**
 * The organization's two-factor requirement (master plan 9.1, for owner and administrators).
 * Whoever turns it on must use two-factor authentication already, or they would lock themselves
 * out; the card says so before the API has to.
 */
@Component({
  selector: 'ardea-mfa-setting-card',
  imports: [RouterLink, TranslocoPipe, UiButton, UiCard, UiNotice],
  template: `
    <ui-card [heading]="'team.mfa.title' | transloco">
      <div class="live" role="status">
        @if (saved()) {
          <ui-notice tone="success">{{
            (required() ? 'team.mfa.savedOn' : 'team.mfa.savedOff') | transloco
          }}</ui-notice>
        }
      </div>
      @if (problem(); as key) {
        <ui-notice class="gap" tone="error">
          {{ key | transloco }}
          @if (needsOwnMfa()) {
            <a
              class="link"
              routerLink="/account/two-factor"
              [queryParams]="{ returnUrl: here() }"
              >{{ 'team.mfa.turnOnOwn' | transloco }}</a
            >
          }
        </ui-notice>
      } @else if (blockedForMe()) {
        <ui-notice class="gap">
          {{ 'team.errors.mfaRequiredForYou' | transloco }}
          <a class="link" routerLink="/account/two-factor" [queryParams]="{ returnUrl: here() }">{{
            'team.mfa.turnOnOwn' | transloco
          }}</a>
        </ui-notice>
      }
      <label class="check">
        <input
          type="checkbox"
          [checked]="required()"
          [disabled]="blockedForMe() || busy()"
          (change)="required.set(!required())"
        />
        <span>
          <span class="label">{{ 'team.mfa.label' | transloco }}</span>
          <span class="hint">{{ 'team.mfa.hint' | transloco }}</span>
        </span>
      </label>
      <div class="actions">
        <button uiButton type="button" [disabled]="!changed()" [loading]="busy()" (click)="save()">
          {{ 'team.mfa.save' | transloco }}
        </button>
      </div>
    </ui-card>
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }
    .live:empty {
      display: none;
    }
    .live,
    .gap {
      margin-bottom: var(--space-md);
    }
    .check {
      display: grid;
      grid-template-columns: 16px minmax(0, 1fr);
      gap: var(--space-sm);
      align-items: start;
      cursor: pointer;
    }
    .check input {
      width: 16px;
      height: 16px;
      margin: 3px 0 0;
      accent-color: var(--text);
    }
    .check input:focus-visible {
      outline: var(--focus-ring);
      outline-offset: var(--focus-offset);
    }
    .check input:disabled {
      cursor: not-allowed;
    }
    .label {
      display: block;
      font-weight: 500;
    }
    .hint {
      display: block;
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .actions {
      margin-top: var(--space-lg);
    }
    .link {
      margin-left: var(--space-xs);
    }
  `,
})
export class MfaSettingCard {
  readonly organization = input.required<SessionOrganization>();
  /** The page's address, for the way back from the two-factor page. */
  readonly here = input.required<string>();

  private readonly api = inject(OrganizationsService);
  private readonly session = inject(Session);

  protected readonly required = linkedSignal(() => this.organization().mfaRequired);
  protected readonly changed = computed(() => this.required() !== this.organization().mfaRequired);
  /** Turning the requirement on needs two-factor authentication on one's own account first. */
  protected readonly blockedForMe = computed(
    () => !this.organization().mfaRequired && this.session.user()?.twoFactorEnabled !== true,
  );
  protected readonly busy = signal(false);
  protected readonly saved = signal(false);
  protected readonly problem = signal<string | null>(null);
  protected readonly needsOwnMfa = signal(false);

  protected async save(): Promise<void> {
    this.busy.set(true);
    this.saved.set(false);
    this.problem.set(null);
    this.needsOwnMfa.set(false);
    try {
      await firstValueFrom(
        this.api.updateOrganization(this.organization().id, {
          name: null,
          locale: null,
          timeZone: null,
          requireMfa: this.required(),
        }),
      );
      // The session carries the requirement for the switcher, the guard and this card.
      await this.session.refresh();
      this.saved.set(true);
    } catch (error) {
      this.needsOwnMfa.set(problemOf(error)?.code === 'mfa_required_for_you');
      this.problem.set(
        problemMessageKey(error, { codes: TEAM_PROBLEMS, fallback: 'team.errors.failed' }),
      );
    } finally {
      this.busy.set(false);
    }
  }
}
