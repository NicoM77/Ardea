import { HttpResourceRef } from '@angular/common/http';
import { Component, computed, inject, input, output, signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { LucideFileCode } from '@lucide/angular';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiCard, UiInput, UiLoader, UiNotice } from 'ui';
import { RepositoriesService, type SuggestionView } from '../../core/api/client';
import { problemMessageKey } from '../../core/api/problem';
import { WIZARD_PROBLEMS } from '../setup/wizard-problems';

@Component({
  selector: 'ardea-suggestions-list',
  imports: [TranslocoPipe, LucideFileCode, UiButton, UiCard, UiInput, UiLoader, UiNotice],
  template: `
    <ui-card
      [heading]="'projects.suggestions.title' | transloco"
      [attr.aria-busy]="suggestions().isLoading()"
    >
      @if (open(); as rows) {
        <span uiCardMeta>{{
          'projects.suggestions.count' | transloco: { count: rows.length }
        }}</span>
      }
      <p class="intro">{{ 'projects.suggestions.intro' | transloco }}</p>
      <div class="live" role="status">
        @if (done(); as message) {
          <ui-notice tone="success">{{ message.key | transloco: message.params }}</ui-notice>
        }
      </div>
      @if (problem()) {
        <ui-notice class="gap" tone="error">{{ problem() | transloco }}</ui-notice>
      }
      @if (open(); as rows) {
        @for (s of rows; track s.id) {
          <div class="suggestion" role="group" [attr.aria-label]="s.productName">
            <div class="head">
              <b>{{ s.productName }} {{ s.version ?? '' }}</b>
              @if (canEdit()) {
                <span class="buttons">
                  <button
                    uiButton
                    type="button"
                    [loading]="busy() === s.id"
                    (click)="decide(s, 'accept')"
                  >
                    {{ 'projects.suggestions.confirm' | transloco }}
                  </button>
                  <button
                    uiButton
                    variant="ghost"
                    type="button"
                    [attr.aria-expanded]="changing() === s.id"
                    (click)="openChange(s)"
                  >
                    {{ 'projects.suggestions.change' | transloco }}
                  </button>
                  <button uiButton variant="ghost" type="button" (click)="decide(s, 'dismiss')">
                    {{ 'projects.suggestions.dismiss' | transloco }}
                  </button>
                </span>
              }
            </div>
            @for (e of s.evidence; track $index) {
              <div class="evidence">
                <span
                  ><svg lucideFileCode aria-hidden="true"></svg
                  >{{
                    'projects.suggestions.where' | transloco: { file: e.file, line: e.line }
                  }}</span
                >
                <code>{{ e.value }}</code>
              </div>
            }
            @if (changing() === s.id) {
              <div class="change">
                <label class="field">
                  <span>{{ 'projects.drafts.version' | transloco }}</span>
                  <input
                    uiInput
                    type="text"
                    spellcheck="false"
                    [value]="s.version ?? ''"
                    (input)="version.set($any($event.target).value)"
                  />
                </label>
                <button
                  uiButton
                  type="button"
                  [loading]="busy() === s.id"
                  (click)="decide(s, 'accept', version())"
                >
                  {{ 'projects.suggestions.saveConfirm' | transloco }}
                </button>
              </div>
            }
          </div>
        } @empty {
          <p class="muted">{{ 'projects.suggestions.none' | transloco }}</p>
        }
      } @else if (suggestions().error()) {
        <ui-notice tone="error">{{ 'projects.suggestions.loadFailed' | transloco }}</ui-notice>
      } @else {
        <ui-loader [label]="'projects.suggestions.loading' | transloco" />
      }
    </ui-card>
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }
    .intro,
    .muted {
      color: var(--muted);
    }
    .intro {
      margin-bottom: var(--space-md);
    }
    .live:empty {
      display: none;
    }
    .live,
    .gap {
      display: block;
      margin-bottom: var(--space-md);
    }
    .suggestion {
      display: grid;
      gap: var(--space-sm);
      padding: var(--space-md) 0;
      border-top: 1px solid var(--rule);
    }
    .suggestion:first-of-type {
      border-top: 0;
      padding-top: 0;
    }
    .head {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-sm);
    }
    .head b {
      font-weight: 500;
    }
    .buttons {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-xs);
    }
    .evidence {
      display: grid;
      gap: 4px;
      padding: var(--space-sm) 10px;
      border-radius: var(--radius-control);
      background: var(--raised);
    }
    .evidence span {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      color: var(--muted);
      font-size: var(--fs-small);
    }
    .evidence svg {
      width: 12px;
      height: 12px;
    }
    code {
      font-family: var(--mono);
      font-size: var(--fs-sub);
      overflow-wrap: anywhere;
    }
    .change {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-end;
      gap: var(--space-sm) var(--space-md);
    }
    .field {
      display: grid;
      gap: 4px;
      flex: 0 1 200px;
    }
    .field > span {
      font-size: var(--fs-sub);
      font-weight: 500;
    }
  `,
})
export class SuggestionsList {
  readonly orgId = input.required<string>();
  readonly projectId = input.required<string>();
  readonly suggestions = input.required<HttpResourceRef<SuggestionView[] | undefined>>();
  readonly canEdit = input(false);
  readonly decided = output<void>();

  private readonly api = inject(RepositoriesService);

  protected readonly open = computed(() => {
    const s = this.suggestions();
    return s.hasValue() ? (s.value() ?? []).filter((x) => x.state === 'open') : null;
  });
  protected readonly busy = signal<string | null>(null);
  protected readonly changing = signal<string | null>(null);
  protected readonly version = signal('');
  protected readonly problem = signal<string | null>(null);
  protected readonly done = signal<{ key: string; params: Record<string, string> } | null>(null);

  protected openChange(s: SuggestionView): void {
    this.version.set(s.version ?? '');
    this.changing.set(this.changing() === s.id ? null : s.id);
  }

  protected async decide(
    s: SuggestionView,
    decision: 'accept' | 'dismiss',
    version?: string,
  ): Promise<void> {
    this.busy.set(s.id);
    this.problem.set(null);
    this.done.set(null);
    try {
      await firstValueFrom(
        this.api.decideSuggestion(this.orgId(), this.projectId(), s.id, {
          decision,
          version: version?.trim() || null,
        }),
      );
      this.changing.set(null);
      this.done.set({
        key:
          decision === 'accept'
            ? 'projects.suggestions.confirmed'
            : 'projects.suggestions.dismissed',
        params: { name: s.productName },
      });
      this.suggestions().reload();
      this.decided.emit();
    } catch (error) {
      this.problem.set(
        problemMessageKey(error, { codes: WIZARD_PROBLEMS, fallback: 'wizard.errors.save' }),
      );
    } finally {
      this.busy.set(null);
    }
  }
}
