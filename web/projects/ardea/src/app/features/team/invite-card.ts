import { Component, computed, ElementRef, inject, input, output, signal } from '@angular/core';
import { form, FormField, FormRoot, type TreeValidationResult } from '@angular/forms/signals';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiCard, UiCopyButton, UiField, UiInput, UiNotice } from 'ui';
import {
  OrganizationsService,
  type InvitationView,
  type SessionOrganization,
} from '../../core/api/client';
import { problemMessageKey, problemOf } from '../../core/api/problem';
import { fieldProblems } from '../admin/admin-errors';
import { EMAIL_ERRORS, emailRules, fieldErrorKey } from '../auth/auth-forms';
import { focusFirstInvalid } from '../auth/focus-invalid';
import { ASSIGNABLE_ROLES, ROLE_HINTS, ROLE_LABELS } from '../organizations/org-format';
import { TEAM_PROBLEMS } from './team-problems';

/**
 * Inviting someone by e-mail address with a role. With SMTP the invitation goes out by mail;
 * without it the API hands the link back once, and this card shows it to pass on by hand
 * (master plan 12.4). The link is not shown again: the list of invitations has no tokens.
 */
@Component({
  selector: 'ardea-invite-card',
  imports: [
    FormField,
    FormRoot,
    TranslocoPipe,
    UiButton,
    UiCard,
    UiCopyButton,
    UiField,
    UiInput,
    UiNotice,
  ],
  template: `
    <ui-card [heading]="'team.invite.title' | transloco">
      <div class="live" role="status">
        @if (sent(); as invitation) {
          @if (invitation.link) {
            <ui-notice tone="success" [heading]="'team.invite.linkHeading' | transloco">
              {{
                'team.invite.linkText'
                  | transloco: { email: invitation.email, until: when(invitation.expiresAt) }
              }}
            </ui-notice>
          } @else {
            <ui-notice tone="success">{{
              'team.invite.sent' | transloco: { email: invitation.email }
            }}</ui-notice>
          }
        }
      </div>
      @if (sent()?.link; as link) {
        <div class="handover">
          <input
            uiInput
            mono
            readonly
            type="text"
            id="invitation-link"
            [value]="link"
            [attr.aria-label]="'team.invite.linkLabel' | transloco"
          />
          <div class="handover-actions">
            <ui-copy-button [text]="link" [label]="'team.invite.copy' | transloco" />
            <button uiButton variant="ghost" type="button" (click)="sent.set(null)">
              {{ 'team.invite.done' | transloco }}
            </button>
          </div>
        </div>
      }

      <form class="form" [formRoot]="invite">
        @if (problem()) {
          <ui-notice tone="error">{{ problem() | transloco }}</ui-notice>
        }
        <ui-field
          [label]="'team.invite.email' | transloco"
          [error]="fieldErrorKey(invite.email, emailErrors) | transloco"
        >
          <input
            uiInput
            type="email"
            autocomplete="off"
            spellcheck="false"
            [formField]="invite.email"
          />
        </ui-field>
        <ui-field
          [label]="'team.invite.role' | transloco"
          [hint]="roleHint() | transloco"
          [error]="fieldErrorKey(invite.role, roleErrors) | transloco"
        >
          <select uiInput [formField]="invite.role">
            @for (role of roles; track role) {
              <option [value]="role">{{ roleLabels[role] | transloco }}</option>
            }
          </select>
        </ui-field>
        <div>
          <button uiButton variant="primary" type="submit" [loading]="invite().submitting()">
            {{ 'team.invite.submit' | transloco }}
          </button>
        </div>
      </form>
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
    .live {
      margin-bottom: var(--space-md);
    }
    .handover {
      display: grid;
      gap: var(--space-sm);
      margin-bottom: var(--space-lg);
      padding-bottom: var(--space-lg);
      border-bottom: 1px solid var(--rule);
    }
    .handover-actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-sm);
    }
    .form {
      display: grid;
      gap: var(--space-lg);
    }
  `,
})
export class InviteCard {
  readonly organization = input.required<SessionOrganization>();
  readonly invited = output<InvitationView>();

  private readonly api = inject(OrganizationsService);
  private readonly transloco = inject(TranslocoService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  protected readonly fieldErrorKey = fieldErrorKey;
  protected readonly emailErrors = {
    ...EMAIL_ERRORS,
    server: 'auth.fields.email.invalid',
    alreadyMember: 'team.errors.alreadyMember',
  };
  protected readonly roleErrors = { server: 'team.invite.roleInvalid' };
  protected readonly roles = ASSIGNABLE_ROLES;
  protected readonly roleLabels = ROLE_LABELS;
  protected readonly problem = signal<string | null>(null);
  /** The last invitation, with its one-time link when the instance sends no e-mail. */
  protected readonly sent = signal<InvitationView | null>(null);

  private readonly model = signal({ email: '', role: 'member' });
  protected readonly roleHint = computed(() => ROLE_HINTS[this.model().role] ?? null);

  protected readonly invite = form(this.model, (p) => emailRules(p.email), {
    submission: {
      action: async (): Promise<TreeValidationResult> => {
        this.problem.set(null);
        this.sent.set(null);
        const { email, role } = this.model();
        try {
          const invitation = await firstValueFrom(
            this.api.createInvitation(this.organization().id, { email: email.trim(), role }),
          );
          this.sent.set(invitation);
          this.invite().reset({ email: '', role });
          this.invited.emit(invitation);
          if (invitation.link) {
            setTimeout(() =>
              this.host.querySelector<HTMLInputElement>('#invitation-link')?.select(),
            );
          }
          return undefined;
        } catch (error) {
          if (problemOf(error)?.code === 'already_member') {
            focusFirstInvalid(this.host);
            return [{ kind: 'alreadyMember', fieldTree: this.invite.email }];
          }
          const onField = fieldProblems(error, {
            email: this.invite.email,
            role: this.invite.role,
          });
          if (onField) {
            focusFirstInvalid(this.host);
            return onField;
          }
          this.problem.set(
            problemMessageKey(error, { codes: TEAM_PROBLEMS, fallback: 'team.invite.failed' }),
          );
          return undefined;
        }
      },
      onInvalid: () => focusFirstInvalid(this.host),
    },
  });

  protected when(value: string): string {
    return new Intl.DateTimeFormat(this.transloco.getActiveLang(), { dateStyle: 'medium' }).format(
      new Date(value),
    );
  }
}
