import {
  Component,
  computed,
  DestroyRef,
  inject,
  input,
  linkedSignal,
  output,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { LucideSearch } from '@lucide/angular';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiCard, UiInput, UiLoader, UiNotice } from 'ui';
import {
  listConnectionRepositoriesResource,
  listRepositoryConnectionsResource,
  RepositoriesService,
  type ListConnectionRepositoriesParams,
  type RepositoryView,
} from '../../core/api/client';
import { problemMessageKey, problemOf } from '../../core/api/problem';
import { WIZARD_PROBLEMS } from './wizard-problems';

interface Pick {
  owner: string;
  name: string;
  defaultBranch: string;
  ref: string;
  path: string;
}

const key = (r: { owner: string; name: string }) => `${r.owner}/${r.name}`;

@Component({
  selector: 'ardea-repository-step',
  imports: [RouterLink, TranslocoPipe, LucideSearch, UiButton, UiCard, UiInput, UiLoader, UiNotice],
  template: `
    <div class="stack">
      <ui-card [heading]="'wizard.repository.accessTitle' | transloco">
        <span uiCardMeta>{{ 'wizard.repository.optional' | transloco }}</span>
        <p class="hint">{{ 'wizard.repository.intro' | transloco }}</p>
        @if (connections.hasValue() && connections.value(); as list) {
          @if (list.length) {
            <div
              class="choices"
              role="radiogroup"
              [attr.aria-label]="'wizard.repository.accessTitle' | transloco"
            >
              @for (c of list; track c.id) {
                <label class="choice" [class.is-checked]="c.id === connectionId()">
                  <input
                    type="radio"
                    name="access"
                    [checked]="c.id === connectionId()"
                    (change)="connectionId.set(c.id)"
                  />
                  <b>{{ c.name }}</b>
                  <small>
                    {{ c.hostName }} ·
                    {{
                      (c.tokenHint ? 'settings.accesses.tokenEnding' : 'settings.accesses.noToken')
                        | transloco: { hint: c.tokenHint }
                    }}
                    @if (c.expiresSoon) {
                      · {{ 'settings.accesses.expiresSoon' | transloco }}
                    }
                  </small>
                </label>
              }
            </div>
          } @else {
            <ui-notice>
              {{ 'wizard.repository.noAccess' | transloco }}
              @if (canManage()) {
                <a class="link" [routerLink]="settingsLink()">{{
                  'wizard.repository.addAccess' | transloco
                }}</a>
              }
            </ui-notice>
          }
        } @else if (connections.error()) {
          <ui-notice tone="error">{{ 'settings.accesses.loadFailed' | transloco }}</ui-notice>
        } @else {
          <ui-loader [label]="'settings.accesses.loading' | transloco" />
        }
      </ui-card>

      @if (connectionId()) {
        <ui-card
          [heading]="'wizard.repository.reposTitle' | transloco"
          [attr.aria-busy]="repositories.isLoading()"
        >
          <span uiCardMeta>{{
            'wizard.repository.selected' | transloco: { count: picks().length }
          }}</span>
          <div class="search">
            <svg lucideSearch aria-hidden="true"></svg>
            <input
              uiInput
              type="search"
              spellcheck="false"
              [attr.aria-label]="'wizard.repository.search' | transloco"
              [placeholder]="'wizard.repository.search' | transloco"
              (input)="type($event)"
            />
          </div>
          @if (repositories.hasValue() && repositories.value(); as list) {
            <div class="repos">
              @for (r of list; track key(r)) {
                <label class="repo">
                  <input type="checkbox" [checked]="isPicked(r)" (change)="toggle(r)" />
                  <span>
                    <b>{{ key(r) }}</b>
                    <small
                      >{{
                        (r.private ? 'wizard.repository.private' : 'wizard.repository.public')
                          | transloco
                      }}
                      · {{ r.defaultBranch }}</small
                    >
                  </span>
                </label>
              } @empty {
                <p class="hint">{{ 'wizard.repository.noRepos' | transloco }}</p>
              }
            </div>
          } @else if (repositories.error()) {
            <ui-notice tone="error">{{ repositoriesProblem() | transloco }}</ui-notice>
          } @else {
            <ui-loader [label]="'wizard.repository.loading' | transloco" />
          }

          @for (p of picks(); track key(p)) {
            <div class="pick" role="group" [attr.aria-label]="key(p)">
              <p>{{ key(p) }}</p>
              <label class="field">
                <span>{{ 'wizard.repository.branch' | transloco }}</span>
                <input
                  uiInput
                  type="text"
                  spellcheck="false"
                  [placeholder]="p.defaultBranch"
                  [value]="p.ref"
                  (input)="set(p, { ref: $any($event.target).value })"
                />
              </label>
              <label class="field">
                <span>{{ 'wizard.repository.path' | transloco }}</span>
                <input
                  uiInput
                  mono
                  type="text"
                  spellcheck="false"
                  [placeholder]="'wizard.repository.wholeRepository' | transloco"
                  [value]="p.path"
                  (input)="set(p, { path: $any($event.target).value })"
                />
              </label>
              <small class="wide">{{ 'wizard.repository.pathHint' | transloco }}</small>
            </div>
          }
        </ui-card>
      }

      @if (problem()) {
        <ui-notice tone="error">{{ problem() | transloco: problemParams() }}</ui-notice>
      }
      <div class="actions">
        <button uiButton variant="ghost" type="button" (click)="back.emit()">
          {{ 'wizard.back' | transloco }}
        </button>
        <span class="right">
          <button uiButton variant="ghost" type="button" (click)="continued.emit()">
            {{ 'wizard.repository.skip' | transloco }}
          </button>
          <button
            uiButton
            variant="primary"
            type="button"
            [disabled]="!picks().length"
            [loading]="saving()"
            (click)="connect()"
          >
            {{ 'wizard.repository.connect' | transloco }}
          </button>
        </span>
      </div>
    </div>
  `,
  styles: `
    :host {
      display: block;
    }
    .stack {
      display: grid;
      gap: var(--gap);
    }
    .hint,
    small {
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .hint {
      margin-bottom: var(--space-md);
    }
    .choices {
      display: grid;
      gap: var(--space-sm);
    }
    .choice {
      position: relative;
      display: grid;
      gap: 2px;
      padding: 10px 12px 10px 36px;
      border: 1px solid var(--control);
      border-radius: var(--radius-card);
      cursor: pointer;
    }
    .choice input {
      position: absolute;
      left: 12px;
      top: 12px;
      width: 16px;
      height: 16px;
      margin: 0;
      accent-color: var(--text);
    }
    .choice.is-checked {
      border-color: var(--text);
      box-shadow: inset 0 0 0 1px var(--text);
    }
    .choice:has(input:focus-visible),
    .repo:has(input:focus-visible) {
      outline: var(--focus-ring);
      outline-offset: var(--focus-offset);
    }
    b {
      font-weight: 500;
    }
    .search {
      position: relative;
      margin-bottom: var(--space-sm);
    }
    .search svg {
      position: absolute;
      left: 10px;
      top: 50%;
      width: 16px;
      height: 16px;
      transform: translateY(-50%);
      color: var(--faint);
      pointer-events: none;
    }
    .search input {
      padding-left: 32px;
    }
    .repos {
      display: grid;
      max-height: 360px;
      overflow-y: auto;
    }
    .repo {
      display: grid;
      grid-template-columns: 16px minmax(0, 1fr);
      gap: var(--space-md);
      align-items: center;
      min-height: 52px;
      padding: var(--space-sm) 0;
      border-top: 1px solid var(--rule);
      cursor: pointer;
    }
    .repo:first-child {
      border-top: 0;
    }
    .repo input {
      width: 16px;
      height: 16px;
      margin: 0;
      accent-color: var(--text);
    }
    .repo span {
      display: grid;
      min-width: 0;
      overflow-wrap: anywhere;
    }
    .pick {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--space-md);
      margin-top: var(--space-md);
      padding: var(--space-md);
      border-radius: var(--radius-card);
      background: var(--raised);
    }
    .pick > p,
    .pick .wide {
      grid-column: 1 / -1;
    }
    .pick > p {
      font-weight: 500;
    }
    .field {
      display: grid;
      gap: 4px;
    }
    .field > span {
      font-size: var(--fs-sub);
      font-weight: 500;
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      justify-content: space-between;
      gap: var(--space-md);
      padding-top: var(--space-lg);
      border-top: 1px solid var(--border);
    }
    .right {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-sm);
    }
    @media (max-width: 640px) {
      .pick {
        grid-template-columns: minmax(0, 1fr);
      }
    }
  `,
})
export class RepositoryStep {
  readonly orgId = input.required<string>();
  readonly projectId = input.required<string>();
  readonly slug = input.required<string>();
  readonly canManage = input(false);
  readonly continued = output<void>();
  readonly back = output<void>();

  private readonly api = inject(RepositoriesService);

  protected readonly key = key;
  protected readonly connections = listRepositoryConnectionsResource(computed(() => this.orgId()));
  protected readonly connectionId = linkedSignal<string | undefined>(() =>
    this.connections.hasValue() ? this.connections.value()?.[0]?.id : undefined,
  );
  private readonly query = signal('');
  private readonly params = computed<ListConnectionRepositoriesParams>(() =>
    this.query() ? { q: this.query() } : {},
  );
  protected readonly repositories = listConnectionRepositoriesResource(
    computed(() => this.orgId()),
    this.connectionId,
    this.params,
  );
  protected readonly repositoriesProblem = computed(() =>
    problemMessageKey(this.repositories.error(), {
      codes: WIZARD_PROBLEMS,
      fallback: 'settings.accesses.errors.github',
    }),
  );
  protected readonly picks = linkedSignal<string | undefined, Pick[]>({
    source: this.connectionId,
    computation: () => [],
  });
  protected readonly saving = signal(false);
  protected readonly problem = signal<string | null>(null);
  protected readonly problemParams = signal<Record<string, string>>({});
  protected readonly settingsLink = computed(() => ['/o', this.slug(), 'settings']);

  private timer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.timer));
  }

  protected type(event: Event): void {
    const value = (event.target as HTMLInputElement).value.trim();
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.query.set(value), 300);
  }

  protected isPicked(r: RepositoryView): boolean {
    return this.picks().some((p) => key(p) === key(r));
  }

  protected toggle(r: RepositoryView): void {
    this.picks.update((list) =>
      list.some((p) => key(p) === key(r))
        ? list.filter((p) => key(p) !== key(r))
        : [
            ...list,
            { owner: r.owner, name: r.name, defaultBranch: r.defaultBranch, ref: '', path: '' },
          ],
    );
  }

  protected set(pick: Pick, change: Partial<Pick>): void {
    this.picks.update((list) => list.map((p) => (key(p) === key(pick) ? { ...p, ...change } : p)));
  }

  protected async connect(): Promise<void> {
    const connectionId = this.connectionId();
    if (!connectionId) return;
    this.saving.set(true);
    this.problem.set(null);
    try {
      for (const p of this.picks()) {
        try {
          await firstValueFrom(
            this.api.createRepositoryLink(this.orgId(), this.projectId(), {
              connectionId,
              owner: p.owner,
              name: p.name,
              ref: p.ref.trim() || null,
              path: p.path.trim() || null,
            }),
          );
        } catch (error) {
          if (problemOf(error)?.code === 'link_exists') continue;
          this.problemParams.set({ name: key(p) });
          this.problem.set(
            problemOf(error)?.errors?.['path']
              ? 'wizard.repository.pathInvalid'
              : problemMessageKey(error, {
                  codes: WIZARD_PROBLEMS,
                  fallback: 'wizard.repository.failed',
                }),
          );
          return;
        }
      }
      this.continued.emit();
    } finally {
      this.saving.set(false);
    }
  }
}
