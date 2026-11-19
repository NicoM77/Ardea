import { HttpResourceRef } from '@angular/common/http';
import { Component, computed, inject, input, output, signal } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import {
  LucideCircleCheck,
  LucideCircleX,
  LucideContrast,
  LucideFolderGit2,
  LucideGitBranch,
} from '@lucide/angular';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiCard, UiLoader, UiNotice } from 'ui';
import { RepositoriesService, type LinkView } from '../../core/api/client';
import { problemMessageKey } from '../../core/api/problem';
import { WIZARD_PROBLEMS } from '../setup/wizard-problems';
import { isReading, LINK_STATE_LABELS, repositoryName } from './link-format';

/**
 * The repositories that feed a project (master plan 16.6): state as a symbol and a word, branch
 * and subfolder, the commit and time of the last reading, and "Read now". A failing link keeps
 * the last good state; a much smaller new state waits for confirmation instead of being applied.
 */
@Component({
  selector: 'ardea-links-card',
  imports: [
    TranslocoPipe,
    LucideCircleCheck,
    LucideCircleX,
    LucideContrast,
    LucideFolderGit2,
    LucideGitBranch,
    UiButton,
    UiCard,
    UiLoader,
    UiNotice,
  ],
  template: `
    <ui-card
      [heading]="(compact() ? 'projects.links.reading' : 'projects.links.title') | transloco"
      [attr.aria-busy]="links().isLoading()"
    >
      <div class="live" role="status">
        @if (done(); as message) {
          <ui-notice tone="success">{{ message.key | transloco: message.params }}</ui-notice>
        }
      </div>
      @if (problem()) {
        <ui-notice class="gap" tone="error">{{ problem() | transloco }}</ui-notice>
      }
      @if (list(); as rows) {
        @if (rows.length) {
          <ul class="links" [attr.aria-label]="'projects.links.title' | transloco">
            @for (link of rows; track link.id) {
              <li>
                <div class="row" [class.compact]="compact()">
                  <div class="what">
                    <span class="name"
                      ><svg lucideFolderGit2 aria-hidden="true"></svg>{{ name(link) }}</span
                    >
                    <span class="meta">
                      <span
                        ><svg lucideGitBranch aria-hidden="true"></svg
                        >{{ link.ref ?? ('projects.links.defaultBranch' | transloco) }}</span
                      >
                      @if (link.path) {
                        <span class="mono">{{ link.path }}</span>
                      }
                      @if (link.lastCommitSha) {
                        <span>{{
                          'projects.links.read'
                            | transloco
                              : {
                                  commit: link.lastCommitSha.slice(0, 7),
                                  when: when(link.lastIndexedAt),
                                }
                        }}</span>
                      }
                      @if (!compact()) {
                        <span>{{ link.connectionName }}</span>
                      }
                    </span>
                  </div>
                  <div class="state">
                    @switch (link.state) {
                      @case ('pending') {
                        <ui-loader [label]="'projects.links.states.pending' | transloco" />
                      }
                      @case ('ok') {
                        <span class="word ok"
                          ><svg lucideCircleCheck aria-hidden="true"></svg
                          >{{ labels['ok'] | transloco }}</span
                        >
                      }
                      @case ('partial') {
                        <span class="word"
                          ><svg lucideContrast aria-hidden="true"></svg
                          >{{ labels['partial'] | transloco }}</span
                        >
                      }
                      @default {
                        <span class="word"
                          ><svg lucideCircleX aria-hidden="true"></svg
                          >{{ labels[link.state] ?? link.state | transloco }}</span
                        >
                      }
                    }
                    @if (!isReading(link)) {
                      <small>{{
                        'projects.componentCount' | transloco: { count: count(link.componentCount) }
                      }}</small>
                    }
                  </div>
                  @if (canEdit() && !compact()) {
                    <div class="buttons">
                      <button
                        uiButton
                        type="button"
                        [loading]="busy() === link.id"
                        [disabled]="isReading(link)"
                        [attr.aria-label]="
                          'projects.links.readNowNamed' | transloco: { name: name(link) }
                        "
                        (click)="sync(link)"
                      >
                        {{ 'projects.links.readNow' | transloco }}
                      </button>
                      <button
                        uiButton
                        variant="ghost"
                        type="button"
                        [attr.aria-label]="
                          'projects.links.removeNamed' | transloco: { name: name(link) }
                        "
                        (click)="confirming.set(link.id)"
                      >
                        {{ 'projects.links.remove' | transloco }}
                      </button>
                    </div>
                  }
                </div>
                @if (link.stateReason && link.state !== 'ok') {
                  <ui-notice class="reason">
                    {{ link.stateReason }}
                    @if (link.failingSince) {
                      {{
                        'projects.links.failingSince' | transloco: { when: when(link.failingSince) }
                      }}
                    }
                  </ui-notice>
                }
                @if (link.shrinkPending) {
                  <ui-notice class="reason" [heading]="'projects.links.shrinkHeading' | transloco">
                    {{ 'projects.links.shrinkText' | transloco }}
                  </ui-notice>
                }
                @if (confirming() === link.id) {
                  <div class="confirm" role="group" [attr.aria-labelledby]="'rm-' + link.id">
                    <p [id]="'rm-' + link.id">
                      {{ 'projects.links.confirmRemove' | transloco: { name: name(link) } }}
                    </p>
                    <div class="buttons">
                      <button
                        uiButton
                        type="button"
                        [loading]="busy() === link.id"
                        (click)="remove(link)"
                      >
                        {{ 'projects.links.remove' | transloco }}
                      </button>
                      <button uiButton variant="ghost" type="button" (click)="confirming.set(null)">
                        {{ 'team.cancel' | transloco }}
                      </button>
                    </div>
                  </div>
                }
              </li>
            }
          </ul>
        } @else {
          <p class="muted">{{ 'projects.links.none' | transloco }}</p>
        }
      } @else if (links().error()) {
        <ui-notice tone="error">{{ 'projects.links.loadFailed' | transloco }}</ui-notice>
      } @else {
        <ui-loader [label]="'projects.links.loading' | transloco" />
      }
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
      display: block;
      margin-bottom: var(--space-md);
    }
    .links li {
      display: grid;
      gap: var(--space-sm);
      padding: var(--space-md) 0;
      border-top: 1px solid var(--rule);
    }
    .links li:first-child {
      border-top: 0;
      padding-top: 0;
    }
    .row {
      display: grid;
      grid-template-columns: minmax(0, 1fr) 150px auto;
      gap: var(--space-sm) var(--space-md);
      align-items: start;
    }
    .row.compact {
      grid-template-columns: minmax(0, 1fr);
    }
    .what {
      display: grid;
      gap: 4px;
      min-width: 0;
    }
    .name {
      display: flex;
      align-items: center;
      gap: var(--space-sm);
      font-weight: 500;
      overflow-wrap: anywhere;
    }
    .meta {
      display: flex;
      flex-wrap: wrap;
      gap: 4px var(--space-md);
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .meta span {
      display: inline-flex;
      align-items: center;
      gap: 4px;
    }
    svg {
      width: 16px;
      height: 16px;
      flex: none;
      color: var(--muted);
    }
    .meta svg {
      width: 12px;
      height: 12px;
    }
    .mono {
      font-family: var(--mono);
    }
    .state {
      display: grid;
      gap: 2px;
    }
    .word {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      font-weight: 500;
    }
    .word.ok svg {
      color: var(--positive);
    }
    small,
    .muted {
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .buttons {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-sm);
      justify-content: flex-end;
    }
    .confirm {
      display: grid;
      gap: var(--space-sm);
      padding: var(--space-md);
      border: 1px solid var(--border);
      border-radius: var(--radius-card);
      background: var(--raised);
    }
    .confirm .buttons {
      justify-content: flex-start;
    }
    @media (max-width: 640px) {
      .row {
        grid-template-columns: minmax(0, 1fr);
      }
      .buttons {
        justify-content: flex-start;
      }
    }
  `,
})
export class LinksCard {
  readonly orgId = input.required<string>();
  readonly projectId = input.required<string>();
  readonly links = input.required<HttpResourceRef<LinkView[] | undefined>>();
  readonly canEdit = input(false);
  /** The wizard's side column: state only, no actions. */
  readonly compact = input(false);
  /** A link was removed or read again: the project's components change. */
  readonly changed = output<void>();

  private readonly api = inject(RepositoriesService);
  private readonly transloco = inject(TranslocoService);

  protected readonly labels = LINK_STATE_LABELS;
  protected readonly isReading = isReading;
  protected readonly name = repositoryName;
  protected readonly list = computed(() => {
    const links = this.links();
    return links.hasValue() ? (links.value() ?? null) : null;
  });
  protected readonly busy = signal<string | null>(null);
  protected readonly confirming = signal<string | null>(null);
  protected readonly problem = signal<string | null>(null);
  protected readonly done = signal<{ key: string; params: Record<string, string> } | null>(null);

  protected count(value: number | string): number {
    return typeof value === 'number' ? value : Number.parseInt(value, 10) || 0;
  }

  protected when(value: string | null): string {
    if (!value) return '';
    return new Intl.DateTimeFormat(this.transloco.getActiveLang(), {
      dateStyle: 'medium',
      timeStyle: 'short',
    }).format(new Date(value));
  }

  protected async sync(link: LinkView): Promise<void> {
    await this.run(link, async () => {
      await firstValueFrom(this.api.syncRepositoryLink(this.orgId(), this.projectId(), link.id));
      return { key: 'projects.links.queued', params: { name: repositoryName(link) } };
    });
  }

  protected async remove(link: LinkView): Promise<void> {
    await this.run(link, async () => {
      await firstValueFrom(this.api.deleteRepositoryLink(this.orgId(), this.projectId(), link.id));
      this.confirming.set(null);
      return { key: 'projects.links.removed', params: { name: repositoryName(link) } };
    });
  }

  private async run(
    link: LinkView,
    work: () => Promise<{ key: string; params: Record<string, string> }>,
  ): Promise<void> {
    this.busy.set(link.id);
    this.problem.set(null);
    this.done.set(null);
    try {
      this.done.set(await work());
      this.links().reload();
      this.changed.emit();
    } catch (error) {
      this.problem.set(
        problemMessageKey(error, { codes: WIZARD_PROBLEMS, fallback: 'wizard.errors.save' }),
      );
    } finally {
      this.busy.set(null);
    }
  }
}
