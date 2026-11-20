import {
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  input,
  linkedSignal,
  signal,
  untracked,
} from '@angular/core';
import { form, FormField, FormRoot, maxLength, required } from '@angular/forms/signals';
import type { TreeValidationResult } from '@angular/forms/signals';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import {
  LucideArchive,
  LucideFolderGit2,
  LucidePencil,
  LucideSlidersHorizontal,
} from '@lucide/angular';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiCard, UiField, UiInput, UiLoader, UiNotice, UiPageHeader, UiPill } from 'ui';
import {
  getProfileCoverageResource,
  getProjectResource,
  listRepositoryLinksResource,
  listSuggestionsResource,
  ProjectsService,
  type ComponentView,
} from '../../core/api/client';
import { problemMessageKey } from '../../core/api/problem';
import { fieldProblems } from '../admin/admin-errors';
import { fieldErrorKey } from '../auth/auth-forms';
import { focusFirstInvalid } from '../auth/focus-invalid';
import { injectOrganization } from '../organizations/current-organization';
import { roleRank } from '../organizations/org-format';
import { WIZARD_PROBLEMS } from '../setup/wizard-problems';
import {
  CRITICALITIES,
  CRITICALITY_LABELS,
  EXPOSURE_LABELS,
  EXPOSURES,
  isPackage,
  KIND_LABELS,
  platforms,
} from './component-format';
import { PackagesView } from './packages-view';
import { CoverageCard } from './coverage-card';
import { LinksCard } from './links-card';
import { isReading, pollWhile } from './link-format';
import { SuggestionsList } from './suggestions-list';

@Component({
  selector: 'ardea-project-page',
  imports: [
    FormField,
    FormRoot,
    RouterLink,
    TranslocoPipe,
    LucideArchive,
    LucidePencil,
    LucideSlidersHorizontal,
    UiButton,
    UiCard,
    UiField,
    UiInput,
    UiLoader,
    UiNotice,
    UiPageHeader,
    UiPill,
    PackagesView,
    LinksCard,
    SuggestionsList,
    CoverageCard,
    LucideFolderGit2,
  ],
  template: `
    @if (detail.hasValue() && detail.value(); as d) {
      <ui-page-header [heading]="d.project.name" [sub]="d.project.description">
        @if (canEdit()) {
          <ng-container ngProjectAs="[uiPageActions]">
            <a uiButton routerLink="setup" [queryParams]="{ step: 'repository' }">
              <svg lucideFolderGit2 aria-hidden="true"></svg>
              {{ 'projects.addRepository' | transloco }}
            </a>
            <a uiButton routerLink="setup" [queryParams]="{ step: 'technologies' }">
              <svg lucideSlidersHorizontal aria-hidden="true"></svg>
              {{ 'projects.changeTechnologies' | transloco }}
            </a>
            <button
              uiButton
              type="button"
              [attr.aria-expanded]="editing()"
              (click)="editing.set(!editing())"
            >
              <svg lucidePencil aria-hidden="true"></svg>
              {{ 'projects.edit' | transloco }}
            </button>
            <button
              uiButton
              variant="ghost"
              type="button"
              [loading]="busy() === 'archive'"
              (click)="toggleArchive()"
            >
              <svg lucideArchive aria-hidden="true"></svg>
              {{ (d.project.archived ? 'projects.restore' : 'projects.archive') | transloco }}
            </button>
          </ng-container>
        }
      </ui-page-header>

      <div class="facts">
        @if (d.project.archived) {
          <ui-pill>{{ 'projects.archived' | transloco }}</ui-pill>
        }
        <ui-pill>{{ exposureLabels[d.project.exposure] | transloco }}</ui-pill>
        <ui-pill>{{ criticalityLabels[d.project.criticality] | transloco }}</ui-pill>
      </div>

      <div class="live" role="status">
        @if (done()) {
          <ui-notice tone="success">{{ done()!.key | transloco: done()!.params }}</ui-notice>
        }
      </div>
      @if (problem()) {
        <ui-notice class="gap" tone="error">{{ problem() | transloco }}</ui-notice>
      }

      @if (editing()) {
        <ui-card class="gap" [heading]="'projects.editTitle' | transloco">
          <form class="form" [formRoot]="editForm">
            <ui-field
              [label]="'wizard.project.name' | transloco"
              [error]="fieldErrorKey(editForm.name, nameErrors) | transloco"
            >
              <input uiInput type="text" [formField]="editForm.name" />
            </ui-field>
            <ui-field [label]="'wizard.project.description' | transloco">
              <textarea uiInput rows="3" [formField]="editForm.description"></textarea>
            </ui-field>
            <div class="pair">
              <ui-field [label]="'wizard.operations.exposure' | transloco">
                <select uiInput [formField]="editForm.exposure">
                  @for (e of exposures; track e) {
                    <option [value]="e">{{ exposureLabels[e] | transloco }}</option>
                  }
                </select>
              </ui-field>
              <ui-field [label]="'wizard.operations.criticality' | transloco">
                <select uiInput [formField]="editForm.criticality">
                  @for (c of criticalities; track c) {
                    <option [value]="c">{{ criticalityLabels[c] | transloco }}</option>
                  }
                </select>
              </ui-field>
            </div>
            <div class="buttons">
              <button uiButton variant="primary" type="submit" [loading]="editForm().submitting()">
                {{ 'projects.save' | transloco }}
              </button>
              <button uiButton variant="ghost" type="button" (click)="editing.set(false)">
                {{ 'team.cancel' | transloco }}
              </button>
            </div>
          </form>
        </ui-card>
      }

      @if (orgId(); as org) {
        <div class="profile-top">
          <ardea-links-card
            [orgId]="org"
            [projectId]="projectId()"
            [links]="links"
            [canEdit]="canEdit()"
            (changed)="refresh()"
          />
          <ardea-coverage-card [coverage]="coverage" />
          @if (openSuggestions()) {
            <ardea-suggestions-list
              [orgId]="org"
              [projectId]="projectId()"
              [suggestions]="suggestions"
              [canEdit]="canEdit()"
              (decided)="refresh()"
            />
          }
        </div>
      }

      <ui-card [heading]="'projects.platforms' | transloco">
        <span uiCardMeta>{{
          'projects.componentCount' | transloco: { count: d.components.length }
        }}</span>
        @if (platformRows().length) {
          <table class="table" [attr.aria-label]="'projects.platforms' | transloco">
            <thead>
              <tr>
                <th scope="col">{{ 'projects.technology' | transloco }}</th>
                <th scope="col" class="end">{{ 'projects.packages.version' | transloco }}</th>
                <th scope="col">{{ 'projects.origin' | transloco }}</th>
                <th scope="col" class="act">
                  <span class="sr-only">{{ 'projects.actions' | transloco }}</span>
                </th>
              </tr>
            </thead>
            <tbody>
              @for (row of platformRows(); track row.id) {
                <tr>
                  <td>
                    <b>{{ row.name }}</b>
                    <small
                      >{{ kindLabels[row.kind] ?? row.kind | transloco
                      }}{{ row.inCatalog ? '' : ' · '
                      }}{{ row.inCatalog ? '' : ('projects.drafts.free' | transloco) }}</small
                    >
                    @if (row.catalogMatch; as match) {
                      <small class="match"
                        >{{
                          (match.learned ? 'projects.catalogMatchLearned' : 'projects.catalogMatch')
                            | transloco: { name: match.name }
                        }}
                        @if (canEdit()) {
                          <button
                            uiButton
                            variant="ghost"
                            type="button"
                            [loading]="busy() === row.id"
                            [attr.aria-label]="
                              'projects.useCatalogEntryNamed'
                                | transloco: { name: row.name, entry: match.name }
                            "
                            (click)="useCatalogEntry(row, match.key)"
                          >
                            {{ 'projects.useCatalogEntry' | transloco }}
                          </button>
                        }
                      </small>
                    }
                  </td>
                  <td class="end">
                    @if (row.kind === 'service') {
                      <span class="muted">{{ 'projects.noVersion' | transloco }}</span>
                    } @else if (row.version) {
                      <span class="mono">{{ row.version }}</span>
                    } @else {
                      <span class="muted">{{ 'projects.versionUnknown' | transloco }}</span>
                    }
                  </td>
                  <td class="wrap">
                    {{
                      (row.origin === 'repository' ? 'projects.fromRepository' : 'projects.manual')
                        | transloco
                    }}
                  </td>
                  <td class="act">
                    @if (canEdit() && row.origin === 'repository') {
                      <button
                        uiButton
                        variant="ghost"
                        type="button"
                        [loading]="busy() === row.id"
                        [attr.aria-label]="'projects.detachNamed' | transloco: { name: row.name }"
                        (click)="detach(row)"
                      >
                        {{ 'projects.detach' | transloco }}
                      </button>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        } @else {
          <p class="muted">{{ 'projects.noPlatforms' | transloco }}</p>
        }
      </ui-card>

      @if (hasPackages()) {
        <ui-card class="gap-top" [heading]="'projects.packagesTitle' | transloco">
          <p class="muted intro">{{ 'projects.packagesHint' | transloco }}</p>
          <ardea-packages-view
            [components]="d.components"
            [detachable]="canEdit()"
            (detach)="detach($event)"
          />
        </ui-card>
      }
    } @else if (detail.error()) {
      <ui-notice tone="error">{{ loadProblem() | transloco }}</ui-notice>
    } @else {
      <ui-loader size="page" [label]="'projects.loading' | transloco" />
    }
  `,
  styles: `
    :host {
      display: block;
    }
    .facts {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-sm);
      margin: calc(-1 * var(--space-md)) 0 var(--space-xl);
    }
    .live:empty {
      display: none;
    }
    .live,
    .gap {
      display: block;
      margin-bottom: var(--space-lg);
    }
    .profile-top {
      display: grid;
      gap: var(--gap);
      margin-bottom: var(--gap);
    }
    .gap-top {
      display: block;
      margin-top: var(--gap);
    }
    .form {
      display: grid;
      gap: var(--space-lg);
    }
    .pair {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--space-lg);
    }
    .buttons {
      display: flex;
      gap: var(--space-sm);
    }
    .intro {
      margin-bottom: var(--space-md);
    }
    .table {
      width: 100%;
      border-collapse: collapse;
    }
    th {
      height: 32px;
      padding: 0 var(--space-md) 0 0;
      color: var(--muted);
      font-size: var(--fs-small);
      font-weight: 400;
      text-align: left;
    }
    td {
      padding: var(--space-sm) var(--space-md) var(--space-sm) 0;
      border-top: 1px solid var(--rule);
      vertical-align: top;
    }
    td b {
      display: block;
      font-weight: 500;
    }
    small,
    .muted {
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .match {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-xs) var(--space-sm);
    }
    .end {
      text-align: right;
    }
    .act {
      width: 1%;
      padding-right: 0;
      text-align: right;
      white-space: nowrap;
    }
    .mono {
      font-family: var(--mono);
      font-size: var(--fs-sub);
    }
    @media (max-width: 640px) {
      .pair {
        grid-template-columns: minmax(0, 1fr);
      }
      thead {
        position: absolute;
        width: 1px;
        height: 1px;
        overflow: hidden;
        clip: rect(0 0 0 0);
      }
      .table,
      tbody,
      tr,
      td {
        display: block;
      }
      tr {
        display: grid;
        grid-template-columns: minmax(0, 1fr) auto;
        gap: 2px var(--space-md);
        padding: var(--space-sm) 0;
        border-top: 1px solid var(--rule);
      }
      td {
        padding: 0;
        border: 0;
      }
      td.end {
        grid-column: 2;
        grid-row: 1;
      }
      td.wrap {
        grid-column: 1;
        color: var(--muted);
        font-size: var(--fs-sub);
      }
      td.act {
        grid-column: 2;
      }
    }
  `,
})
export class ProjectPage {
  readonly projectId = input.required<string>();

  private readonly projects = inject(ProjectsService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly org = injectOrganization().organization;

  protected readonly kindLabels = KIND_LABELS;
  protected readonly exposureLabels = EXPOSURE_LABELS;
  protected readonly criticalityLabels = CRITICALITY_LABELS;
  protected readonly exposures = EXPOSURES;
  protected readonly criticalities = CRITICALITIES;
  protected readonly fieldErrorKey = fieldErrorKey;
  protected readonly nameErrors = {
    required: 'wizard.project.errors.name',
    server: 'wizard.project.errors.name',
  };

  protected readonly orgId = computed(() => this.org()?.id);
  protected readonly canEdit = computed(() => roleRank(this.org()?.role) >= roleRank('member'));
  protected readonly detail = getProjectResource(
    this.orgId,
    computed(() => this.projectId()),
  );
  private readonly components = computed<ComponentView[]>(() =>
    this.detail.hasValue() ? (this.detail.value()?.components ?? []) : [],
  );
  protected readonly platformRows = computed(() => platforms(this.components()));
  protected readonly hasPackages = computed(() => this.components().some(isPackage));
  protected readonly loadProblem = computed(() =>
    problemMessageKey(this.detail.error(), {
      codes: WIZARD_PROBLEMS,
      fallback: 'projects.loadFailed',
    }),
  );

  protected readonly links = listRepositoryLinksResource(
    this.orgId,
    computed(() => this.projectId()),
  );
  protected readonly suggestions = listSuggestionsResource(
    this.orgId,
    computed(() => this.projectId()),
    computed(() => ({ state: 'open' })),
  );
  protected readonly coverage = getProfileCoverageResource(
    this.orgId,
    computed(() => ({ projectId: this.projectId() })),
  );
  protected readonly openSuggestions = computed(
    () =>
      this.suggestions.hasValue() &&
      (this.suggestions.value() ?? []).some((s) => s.state === 'open'),
  );
  private readonly reading = computed(
    () => (this.links.hasValue() ? (this.links.value() ?? []) : []).filter(isReading).length,
  );

  protected readonly editing = signal(false);
  protected readonly busy = signal<string | null>(null);
  protected readonly problem = signal<string | null>(null);
  protected readonly done = signal<{ key: string; params: Record<string, string> } | null>(null);

  private readonly model = linkedSignal(() => {
    const p = this.detail.hasValue() ? this.detail.value()?.project : undefined;
    return {
      name: p?.name ?? '',
      description: p?.description ?? '',
      exposure: p?.exposure ?? 'internal',
      criticality: p?.criticality ?? 'normal',
    };
  });
  protected readonly editForm = form(
    this.model,
    (p) => {
      required(p.name);
      maxLength(p.name, 200);
      maxLength(p.description, 2000);
    },
    {
      submission: {
        action: async (): Promise<TreeValidationResult> => {
          this.problem.set(null);
          const m = this.model();
          try {
            await firstValueFrom(
              this.projects.updateProject(this.orgId()!, this.projectId(), {
                name: m.name.trim(),
                description: m.description.trim(),
                exposure: m.exposure,
                criticality: m.criticality,
                archived: null,
              }),
            );
            this.editing.set(false);
            this.done.set({ key: 'projects.saved', params: {} });
            this.detail.reload();
            return undefined;
          } catch (error) {
            const onField = fieldProblems(error, { name: this.editForm.name });
            if (onField) {
              focusFirstInvalid(this.host);
              return onField;
            }
            this.problem.set(
              problemMessageKey(error, { codes: WIZARD_PROBLEMS, fallback: 'wizard.errors.save' }),
            );
            return undefined;
          }
        },
        onInvalid: () => focusFirstInvalid(this.host),
      },
    },
  );

  constructor() {
    pollWhile(
      computed(() => this.reading() > 0),
      () => this.links.reload(),
    );
    let before = 0;
    effect(() => {
      const now = this.reading();
      if (now < before) {
        untracked(() => {
          this.detail.reload();
          this.suggestions.reload();
          this.coverage.reload();
        });
      }
      before = now;
    });
  }

  protected refresh(): void {
    this.detail.reload();
    this.coverage.reload();
  }

  protected async toggleArchive(): Promise<void> {
    const archived = this.detail.value()?.project.archived ?? false;
    await this.run('archive', async () => {
      await firstValueFrom(
        this.projects.updateProject(this.orgId()!, this.projectId(), {
          name: null,
          description: null,
          exposure: null,
          criticality: null,
          archived: !archived,
        }),
      );
      return { key: archived ? 'projects.restored' : 'projects.archivedNow', params: {} };
    });
  }

  protected async useCatalogEntry(component: ComponentView, productKey: string): Promise<void> {
    await this.run(component.id, async () => {
      await firstValueFrom(
        this.projects.updateComponent(this.orgId()!, this.projectId(), component.id, {
          id: null,
          ref: null,
          productKey,
          customVendor: null,
          customName: null,
          kind: null,
          version: null,
          packaging: null,
          parentId: null,
          parentRef: null,
          exposure: null,
          criticality: null,
          protection: null,
          protectionNote: null,
        }),
      );
      return { key: 'projects.catalogEntryUsed', params: { name: component.name } };
    });
  }

  protected async detach(component: ComponentView): Promise<void> {
    await this.run(component.id, async () => {
      await firstValueFrom(
        this.projects.detachComponent(this.orgId()!, this.projectId(), component.id),
      );
      return { key: 'projects.detached', params: { name: component.name } };
    });
  }

  private async run(
    id: string,
    work: () => Promise<{ key: string; params: Record<string, string> }>,
  ): Promise<void> {
    this.busy.set(id);
    this.problem.set(null);
    this.done.set(null);
    try {
      this.done.set(await work());
      this.refresh();
    } catch (error) {
      this.problem.set(
        problemMessageKey(error, { codes: WIZARD_PROBLEMS, fallback: 'wizard.errors.save' }),
      );
    } finally {
      this.busy.set(null);
    }
  }
}
