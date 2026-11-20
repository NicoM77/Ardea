import { Component, computed, ElementRef, inject, input, signal } from '@angular/core';
import { form, FormField, FormRoot, maxLength, required } from '@angular/forms/signals';
import type { TreeValidationResult } from '@angular/forms/signals';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import {
  UiButton,
  UiCard,
  UiField,
  UiInput,
  UiLoader,
  UiMenu,
  UiMenuItem,
  UiNotice,
  UiOverflowMenu,
  UiPasswordToggle,
  UiPill,
  UiToaster,
} from 'ui';
import {
  listGitHubHostsResource,
  listRepositoryConnectionsResource,
  RepositoryConnectionsService,
  type ConnectionView,
  type SessionOrganization,
} from '../../core/api/client';
import { problemMessageKey, problemOf } from '../../core/api/problem';
import { Dates } from '../../core/format/dates';
import { fieldProblems } from '../admin/admin-errors';
import { fieldErrorKey } from '../auth/auth-forms';
import { focusFirstInvalid } from '../auth/focus-invalid';
import { canManage } from '../organizations/org-format';
import { WIZARD_PROBLEMS } from '../setup/wizard-problems';

@Component({
  selector: 'ardea-accesses-card',
  imports: [
    FormField,
    FormRoot,
    TranslocoPipe,
    UiButton,
    UiCard,
    UiField,
    UiInput,
    UiLoader,
    UiMenu,
    UiMenuItem,
    UiNotice,
    UiOverflowMenu,
    UiPasswordToggle,
    UiPill,
  ],
  template: `
    <ui-card
      [heading]="'settings.accesses.title' | transloco"
      [attr.aria-busy]="connections.isLoading()"
    >
      <p class="intro">{{ 'settings.accesses.intro' | transloco }}</p>
      <div class="live" role="status">
        @if (tested(); as result) {
          <ui-notice [tone]="result.ok ? 'success' : 'error'">{{
            (result.ok ? 'settings.accesses.testOk' : 'settings.accesses.testFailed')
              | transloco: { name: result.name }
          }}</ui-notice>
        }
      </div>
      @if (problem()) {
        <ui-notice class="gap" tone="error">{{ problem() | transloco }}</ui-notice>
      }

      @if (connections.hasValue() && connections.value(); as list) {
        @if (list.length) {
          <ul class="list" [attr.aria-label]="'settings.accesses.title' | transloco">
            @for (c of list; track c.id) {
              <li>
                <div class="row">
                  <div class="what">
                    <b>{{ c.name }}</b>
                    <small>{{ describe(c) }}</small>
                    @if (c.lastError) {
                      <small class="error">{{
                        'settings.accesses.lastError' | transloco: { error: c.lastError }
                      }}</small>
                    } @else if (c.lastOkAt) {
                      <small>{{
                        'settings.accesses.lastOk' | transloco: { when: when(c.lastOkAt) }
                      }}</small>
                    }
                  </div>
                  <div class="tags">
                    @if (c.expiresSoon) {
                      <ui-pill tone="alert">{{
                        'settings.accesses.expiresSoon' | transloco
                      }}</ui-pill>
                    }
                  </div>
                  <div class="buttons">
                    <button
                      uiButton
                      type="button"
                      [loading]="busy() === c.id + ':test'"
                      (click)="test(c)"
                    >
                      {{ 'settings.accesses.test' | transloco }}
                    </button>
                    @if (manage()) {
                      <ui-overflow-menu #more [name]="c.name">
                        <ng-template>
                          <div uiMenu>
                            <button uiMenuItem destructive type="button" (triggered)="ask(c, more)">
                              {{ 'settings.accesses.delete' | transloco }}
                            </button>
                          </div>
                        </ng-template>
                      </ui-overflow-menu>
                    }
                  </div>
                </div>
                @for (warning of c.warnings; track warning) {
                  <ui-notice class="warning">{{ warning }}</ui-notice>
                }
                @if (confirming() === c.id) {
                  <div class="confirm" role="group" [attr.aria-labelledby]="'del-' + c.id">
                    <p [id]="'del-' + c.id">
                      {{ 'settings.accesses.confirmDelete' | transloco: { name: c.name } }}
                    </p>
                    <div class="buttons">
                      <button
                        uiButton
                        type="button"
                        class="confirm-button"
                        [loading]="busy() === c.id + ':delete'"
                        (click)="remove(c)"
                      >
                        {{ 'settings.accesses.delete' | transloco }}
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
        } @else {
          <p class="muted">{{ 'settings.accesses.none' | transloco }}</p>
        }
      } @else if (connections.error()) {
        <ui-notice tone="error">{{ 'settings.accesses.loadFailed' | transloco }}</ui-notice>
      } @else {
        <ui-loader [label]="'settings.accesses.loading' | transloco" />
      }

      @if (manage()) {
        <h3>{{ 'settings.accesses.addTitle' | transloco }}</h3>
        <ol class="steps">
          <li>{{ 'settings.accesses.guide1' | transloco }}</li>
          <li>{{ 'settings.accesses.guide2' | transloco }}</li>
          <li>{{ 'settings.accesses.guide3' | transloco }}</li>
          <li>{{ 'settings.accesses.guide4' | transloco }}</li>
        </ol>
        <form class="form" [formRoot]="create">
          <div class="pair">
            <ui-field
              [label]="'settings.accesses.name' | transloco"
              [error]="fieldErrorKey(create.name, nameErrors) | transloco"
            >
              <input uiInput type="text" autocomplete="off" [formField]="create.name" />
            </ui-field>
            @if (hosts().length) {
              <ui-field [label]="'settings.accesses.host' | transloco">
                <select uiInput [formField]="create.hostId">
                  <option value="">github.com</option>
                  @for (h of hosts(); track h.id) {
                    <option [value]="h.id">{{ h.name }}</option>
                  }
                </select>
              </ui-field>
            }
          </div>
          <ui-field
            [label]="'settings.accesses.token' | transloco"
            [hint]="'settings.accesses.tokenHint' | transloco"
            [error]="fieldErrorKey(create.token, tokenErrors) | transloco"
            [optional]="true"
          >
            <input
              uiInput
              mono
              #token
              type="password"
              autocomplete="off"
              spellcheck="false"
              [formField]="create.token"
            />
            <ui-password-toggle [control]="token" />
          </ui-field>
          <div>
            <button uiButton type="submit" [loading]="create().submitting()">
              {{ 'settings.accesses.save' | transloco }}
            </button>
          </div>
        </form>
      }
    </ui-card>
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }
    .intro,
    .muted,
    small {
      color: var(--muted);
    }
    .intro {
      margin-bottom: var(--space-md);
      max-width: 72ch;
    }
    small {
      display: block;
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    small.error {
      color: var(--text);
    }
    .live:empty {
      display: none;
    }
    .live,
    .gap {
      display: block;
      margin-bottom: var(--space-md);
    }
    .list li {
      display: grid;
      gap: var(--space-sm);
      padding: var(--space-md) 0;
      border-top: 1px solid var(--rule);
    }
    .list li:first-child {
      border-top: 0;
      padding-top: 0;
    }
    .row {
      display: grid;
      grid-template-columns: minmax(0, 1fr) auto auto;
      gap: var(--space-sm) var(--space-md);
      align-items: start;
    }
    .what b {
      font-weight: 500;
    }
    .buttons {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-sm);
    }
    .confirm {
      display: grid;
      gap: var(--space-sm);
      padding: var(--space-md);
      border: 1px solid var(--border);
      border-radius: var(--radius-card);
      background: var(--raised);
    }
    h3 {
      margin: var(--space-xl) 0 var(--space-sm);
      font-weight: 600;
    }
    .steps {
      display: grid;
      gap: 4px;
      margin-bottom: var(--space-lg);
      padding-left: 20px;
      list-style: decimal;
      color: var(--muted);
    }
    .form {
      display: grid;
      gap: var(--space-lg);
    }
    .pair {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
      gap: var(--space-lg);
    }
    @media (max-width: 640px) {
      .row {
        grid-template-columns: minmax(0, 1fr);
      }
    }
  `,
})
export class AccessesCard {
  readonly organization = input.required<SessionOrganization>();

  private readonly api = inject(RepositoryConnectionsService);
  private readonly transloco = inject(TranslocoService);
  private readonly toaster = inject(UiToaster);
  private readonly dates = inject(Dates);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private opener: UiOverflowMenu | null = null;

  protected readonly fieldErrorKey = fieldErrorKey;
  protected readonly nameErrors = {
    required: 'settings.accesses.errors.name',
    maxLength: 'settings.accesses.errors.name',
    server: 'settings.accesses.errors.name',
  };
  protected readonly tokenErrors = {
    server: 'settings.accesses.errors.token',
    invalidToken: 'settings.accesses.errors.token',
  };
  protected readonly manage = computed(() => canManage(this.organization().role));
  protected readonly connections = listRepositoryConnectionsResource(
    computed(() => this.organization().id),
  );
  private readonly hostList = listGitHubHostsResource();
  protected readonly hosts = computed(() =>
    this.hostList.hasValue() ? (this.hostList.value() ?? []) : [],
  );

  protected readonly busy = signal<string | null>(null);
  protected readonly confirming = signal<string | null>(null);
  protected readonly problem = signal<string | null>(null);
  protected readonly tested = signal<{ ok: boolean; name: string } | null>(null);

  private readonly model = signal({ name: '', token: '', hostId: '' });
  protected readonly create = form(
    this.model,
    (p) => {
      required(p.name);
      maxLength(p.name, 100);
    },
    {
      submission: {
        action: async (): Promise<TreeValidationResult> => {
          this.reset();
          const m = this.model();
          try {
            const created = await firstValueFrom(
              this.api.createRepositoryConnection(this.organization().id, {
                name: m.name.trim(),
                token: m.token.trim() || null,
                hostId: m.hostId || null,
              }),
            );
            this.create().reset({ name: '', token: '', hostId: '' });
            this.confirm('settings.accesses.created', created.name);
            this.connections.reload();
            return undefined;
          } catch (error) {
            if (problemOf(error)?.code === 'invalid_token') {
              focusFirstInvalid(this.host);
              return [{ kind: 'invalidToken', fieldTree: this.create.token }];
            }
            const onField = fieldProblems(error, {
              name: this.create.name,
              token: this.create.token,
            });
            if (onField) {
              focusFirstInvalid(this.host);
              return onField;
            }
            this.problem.set(
              problemMessageKey(error, { codes: WIZARD_PROBLEMS, fallback: 'team.errors.failed' }),
            );
            return undefined;
          }
        },
        onInvalid: () => focusFirstInvalid(this.host),
      },
    },
  );

  protected describe(c: ConnectionView): string {
    const t = (key: string, params?: Record<string, unknown>) =>
      this.transloco.translate(key, params);
    const parts = [c.hostName];
    parts.push(
      c.tokenHint
        ? t('settings.accesses.tokenEnding', { hint: c.tokenHint })
        : t('settings.accesses.noToken'),
    );
    if (c.login) parts.push(t('settings.accesses.readsAs', { login: c.login }));
    if (c.tokenExpiresAt)
      parts.push(t('settings.accesses.expires', { date: this.when(c.tokenExpiresAt) }));
    return parts.join(' · ');
  }

  protected when(value: string): string {
    return this.dates.date(value);
  }

  protected async test(c: ConnectionView): Promise<void> {
    await this.run(c.id + ':test', async () => {
      const checked = await firstValueFrom(
        this.api.testRepositoryConnection(this.organization().id, c.id),
      );
      this.tested.set({ ok: !checked.lastError, name: c.name });
    });
  }

  protected ask(c: ConnectionView, opener: UiOverflowMenu): void {
    this.opener = opener;
    this.confirming.set(c.id);
    setTimeout(() => this.host.querySelector<HTMLElement>('.confirm-button')?.focus());
  }

  protected cancel(): void {
    this.confirming.set(null);
    this.opener?.focus();
    this.opener = null;
  }

  protected async remove(c: ConnectionView): Promise<void> {
    await this.run(c.id + ':delete', async () => {
      await firstValueFrom(this.api.deleteRepositoryConnection(this.organization().id, c.id));
      this.confirming.set(null);
      this.opener = null;
      this.confirm('settings.accesses.deleted', c.name);
    });
  }

  private reset(): void {
    this.problem.set(null);
    this.tested.set(null);
  }

  private confirm(key: string, name: string): void {
    this.toaster.show({ text: this.transloco.translate(key, { name }) });
  }

  private async run(id: string, work: () => Promise<void>): Promise<void> {
    this.busy.set(id);
    this.reset();
    try {
      await work();
      this.connections.reload();
    } catch (error) {
      this.problem.set(
        problemMessageKey(error, { codes: WIZARD_PROBLEMS, fallback: 'team.errors.failed' }),
      );
    } finally {
      this.busy.set(null);
    }
  }
}
