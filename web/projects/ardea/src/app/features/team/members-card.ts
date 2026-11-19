import { HttpResourceRef } from '@angular/common/http';
import { Component, computed, ElementRef, inject, input, signal } from '@angular/core';
import { Router } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { UiAvatar, UiButton, UiCard, UiInput, UiLoader, UiNotice } from 'ui';
import {
  OrganizationsService,
  type MemberView,
  type SessionOrganization,
} from '../../core/api/client';
import { problemMessageKey } from '../../core/api/problem';
import { Session } from '../../core/session/session';
import { ASSIGNABLE_ROLES, memberActions, ROLE_LABELS } from '../organizations/org-format';
import { TEAM_PROBLEMS } from './team-problems';

interface Pending {
  kind: 'remove' | 'leave' | 'transfer';
  member: MemberView;
}

const QUESTIONS: Record<Pending['kind'], string> = {
  remove: 'team.members.confirmRemove',
  leave: 'team.members.confirmLeave',
  transfer: 'team.members.confirmTransfer',
};
const CONFIRM_LABELS: Record<Pending['kind'], string> = {
  remove: 'team.members.remove',
  leave: 'team.members.leave',
  transfer: 'team.members.transfer',
};

/**
 * The member directory (master plan 9.2): name, e-mail address and role, nothing else. The owner
 * and administrators change roles and remove members; everyone may leave; the owner hands
 * ownership on. Removing, leaving and the transfer ask once, in place, before they happen.
 */
@Component({
  selector: 'ardea-members-card',
  imports: [TranslocoPipe, UiAvatar, UiButton, UiCard, UiInput, UiLoader, UiNotice],
  template: `
    <ui-card [heading]="'team.members.title' | transloco" [attr.aria-busy]="members().isLoading()">
      @if (list(); as rows) {
        <span uiCardMeta class="meta">{{
          'team.members.count' | transloco: { count: rows.length }
        }}</span>
      }
      <div class="live" role="status">
        @if (done(); as message) {
          <ui-notice tone="success">{{ message.key | transloco: message.params }}</ui-notice>
        }
      </div>
      @if (problem()) {
        <ui-notice class="gap" tone="error">{{ problem() | transloco }}</ui-notice>
      }

      @if (list(); as rows) {
        <ul class="members" [attr.aria-label]="'team.members.title' | transloco">
          @for (member of rows; track member.userId) {
            @let can = actions(member);
            <li>
              <div class="row">
                <ui-avatar [name]="member.displayName" [personId]="member.userId" />
                <div class="who">
                  <span class="name">
                    {{ member.displayName }}
                    @if (member.userId === meId()) {
                      <span class="you">{{ 'team.members.you' | transloco }}</span>
                    }
                  </span>
                  <span class="sub">{{ member.email }}</span>
                </div>
                <div class="role">
                  @if (can.changeRole) {
                    <select
                      uiInput
                      [disabled]="busy() === member.userId"
                      [attr.aria-label]="
                        'team.members.roleOf' | transloco: { name: member.displayName }
                      "
                      (change)="changeRole(member, $event)"
                    >
                      @for (role of roles; track role) {
                        <option [value]="role" [selected]="role === member.role">
                          {{ roleLabels[role] | transloco }}
                        </option>
                      }
                    </select>
                  } @else {
                    <span class="role-text">{{
                      roleLabels[member.role] ?? member.role | transloco
                    }}</span>
                  }
                </div>
                <div class="act">
                  @if (can.transfer) {
                    <button
                      uiButton
                      variant="ghost"
                      type="button"
                      [attr.aria-label]="
                        'team.members.transferLabel' | transloco: { name: member.displayName }
                      "
                      (click)="ask('transfer', member, $event)"
                    >
                      {{ 'team.members.transfer' | transloco }}
                    </button>
                  }
                  @if (can.remove) {
                    <button
                      uiButton
                      variant="ghost"
                      type="button"
                      [attr.aria-label]="
                        'team.members.removeLabel' | transloco: { name: member.displayName }
                      "
                      (click)="ask('remove', member, $event)"
                    >
                      {{ 'team.members.remove' | transloco }}
                    </button>
                  }
                  @if (can.leave) {
                    <button
                      uiButton
                      variant="ghost"
                      type="button"
                      (click)="ask('leave', member, $event)"
                    >
                      {{ 'team.members.leave' | transloco }}
                    </button>
                  }
                </div>
              </div>

              @if (pending()?.member?.userId === member.userId) {
                @let p = pending()!;
                <div
                  class="confirm"
                  role="group"
                  [attr.aria-labelledby]="'confirm-' + member.userId"
                >
                  <p [id]="'confirm-' + member.userId">
                    {{
                      questions[p.kind]
                        | transloco: { name: member.displayName, organization: organization().name }
                    }}
                  </p>
                  <div class="confirm-actions">
                    <button
                      uiButton
                      type="button"
                      class="confirm-button"
                      [loading]="busy() === member.userId"
                      (click)="confirm()"
                    >
                      {{ confirmLabels[p.kind] | transloco }}
                    </button>
                    <button uiButton variant="ghost" type="button" (click)="cancel()">
                      {{ 'team.cancel' | transloco }}
                    </button>
                  </div>
                </div>
              }
            </li>
          }
        </ul>
      } @else if (members().error()) {
        <ui-notice tone="error">{{ loadProblem() | transloco }}</ui-notice>
        <div class="gap">
          <button uiButton type="button" (click)="members().reload()">
            {{ 'team.tryAgain' | transloco }}
          </button>
        </div>
      } @else {
        <ui-loader [label]="'team.members.loading' | transloco" />
      }
    </ui-card>
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }
    .meta {
      color: var(--muted);
      font-size: var(--fs-sub);
    }
    .live:empty {
      display: none;
    }
    .live,
    .gap {
      margin-bottom: var(--space-md);
    }
    .members {
      display: grid;
    }
    li + li {
      border-top: 1px solid var(--rule);
    }
    .row {
      display: grid;
      grid-template-columns: 24px minmax(0, 1fr) 140px minmax(0, auto);
      align-items: center;
      gap: var(--space-sm) var(--space-md);
      min-height: 52px;
      padding: var(--space-sm) 0;
    }
    .who {
      display: grid;
      min-width: 0;
    }
    .name {
      font-weight: 500;
      overflow-wrap: anywhere;
    }
    .you {
      margin-left: var(--space-xs);
      color: var(--muted);
      font-weight: 400;
    }
    .sub {
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
      overflow-wrap: anywhere;
    }
    .role-text {
      /* In line with the text of the selects above and below */
      padding-left: 11px;
      color: var(--muted);
    }
    .act {
      display: flex;
      justify-content: flex-end;
      flex-wrap: wrap;
      gap: var(--space-xs);
    }
    .confirm {
      display: grid;
      gap: var(--space-sm);
      margin: 0 0 var(--space-md) calc(24px + var(--space-md));
      padding: var(--space-md);
      border: 1px solid var(--border);
      border-radius: var(--radius-card);
      background: var(--raised);
    }
    .confirm p {
      text-wrap: pretty;
    }
    .confirm-actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-sm);
    }
    @media (max-width: 640px) {
      .row {
        grid-template-columns: 24px minmax(0, 1fr);
      }
      .role,
      .act {
        grid-column: 2;
      }
      .act {
        justify-content: flex-start;
      }
      .role-text {
        padding-left: 0;
      }
      .confirm {
        margin-left: 0;
      }
    }
  `,
})
export class MembersCard {
  readonly organization = input.required<SessionOrganization>();
  readonly members = input.required<HttpResourceRef<MemberView[] | undefined>>();

  private readonly api = inject(OrganizationsService);
  private readonly session = inject(Session);
  private readonly router = inject(Router);
  private readonly transloco = inject(TranslocoService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  protected readonly roles = ASSIGNABLE_ROLES;
  protected readonly roleLabels = ROLE_LABELS;
  protected readonly questions = QUESTIONS;
  protected readonly confirmLabels = CONFIRM_LABELS;

  protected readonly pending = signal<Pending | null>(null);
  protected readonly busy = signal<string | null>(null);
  protected readonly done = signal<{ key: string; params: Record<string, string> } | null>(null);
  protected readonly problem = signal<string | null>(null);

  protected readonly meId = computed(() => this.session.user()?.id ?? '');
  protected readonly list = computed(() => {
    const members = this.members();
    return members.hasValue() ? (members.value() ?? null) : null;
  });
  protected readonly loadProblem = computed(() =>
    problemMessageKey(this.members().error(), {
      codes: TEAM_PROBLEMS,
      fallback: 'team.members.loadFailed',
    }),
  );

  /** The control that opened the confirmation, to return focus to when it closes. */
  private opener: HTMLElement | null = null;

  protected actions(member: MemberView) {
    return memberActions(member, this.meId(), this.organization().role);
  }

  protected ask(kind: Pending['kind'], member: MemberView, event: Event): void {
    this.opener = event.currentTarget as HTMLElement;
    this.problem.set(null);
    this.pending.set({ kind, member });
    setTimeout(() => this.host.querySelector<HTMLElement>('.confirm-button')?.focus());
  }

  protected cancel(): void {
    this.pending.set(null);
    this.opener?.focus();
    this.opener = null;
  }

  protected async confirm(): Promise<void> {
    const pending = this.pending();
    if (!pending) return;
    const { kind, member } = pending;
    const orgId = this.organization().id;
    await this.run(member, async () => {
      if (kind === 'transfer') {
        await firstValueFrom(this.api.updateMember(orgId, member.userId, { role: 'owner' }));
        this.pending.set(null);
        // The former owner is an administrator now: the session carries the new role.
        await this.session.refresh();
        this.members().reload();
        this.finish('team.members.transferred', { name: member.displayName });
        return;
      }
      await firstValueFrom(this.api.removeMember(orgId, member.userId));
      this.pending.set(null);
      if (kind === 'leave') {
        await this.session.refresh();
        await this.router.navigateByUrl('/');
        return;
      }
      this.members().reload();
      this.finish('team.members.removed', { name: member.displayName });
    });
  }

  protected async changeRole(member: MemberView, event: Event): Promise<void> {
    const select = event.target as HTMLSelectElement;
    const role = select.value;
    this.pending.set(null);
    const changed = await this.run(member, async () => {
      await firstValueFrom(this.api.updateMember(this.organization().id, member.userId, { role }));
      this.members().reload();
      this.finish('team.members.roleChanged', {
        name: member.displayName,
        role: this.transloco.translate(ROLE_LABELS[role] ?? role),
      });
    });
    // A refused change leaves the select where the server still has it.
    if (!changed) select.value = member.role;
  }

  private finish(key: string, params: Record<string, string>): void {
    this.done.set({ key, params });
    this.opener = null;
  }

  private async run(member: MemberView, work: () => Promise<void>): Promise<boolean> {
    this.busy.set(member.userId);
    this.problem.set(null);
    this.done.set(null);
    try {
      await work();
      return true;
    } catch (error) {
      this.problem.set(
        problemMessageKey(error, { codes: TEAM_PROBLEMS, fallback: 'team.errors.failed' }),
      );
      return false;
    } finally {
      this.busy.set(null);
    }
  }
}
