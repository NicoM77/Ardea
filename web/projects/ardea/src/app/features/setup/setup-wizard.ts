import {
  Component,
  computed,
  effect,
  inject,
  input,
  linkedSignal,
  signal,
  untracked,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { UiLoader, UiNotice, UiPageHeader } from 'ui';
import {
  getProjectResource,
  getWizardStateResource,
  listRepositoryLinksResource,
  listSuggestionsResource,
  OrganizationsService,
  ProjectsService,
  type ProjectView,
} from '../../core/api/client';
import { problemMessageKey } from '../../core/api/problem';
import { injectOrganization } from '../organizations/current-organization';
import { draftFromView, toInput, type DraftComponent } from '../projects/component-format';
import { OperationsStep, type OperationsAnswer } from './operations-step';
import { ProjectStep } from './project-step';
import { TechnologiesStep } from './technologies-step';
import { RepositoryStep } from './repository-step';
import { LinksCard } from '../projects/links-card';
import { SuggestionsList } from '../projects/suggestions-list';
import { isReading, pollWhile } from '../projects/link-format';
import { canManage } from '../organizations/org-format';
import { WIZARD_PROBLEMS } from './wizard-problems';

/** `organization`: the first project of a new organization; `new`: another project; `project`: an existing one. */
export type WizardMode = 'organization' | 'new' | 'project';

export type WizardStep = 'project' | 'repository' | 'operations' | 'technologies';

const STEP_LABELS: Record<WizardStep, string> = {
  project: 'wizard.steps.project',
  repository: 'wizard.steps.repository',
  operations: 'wizard.steps.operations',
  technologies: 'wizard.steps.technologies',
};

/**
 * The setup wizard (master plan 11.6): saved after every step, continued at any time, nothing
 * asked twice. The first run sets up an organization's first project and records its progress
 * (`/orgs/{orgId}/setup`); later runs add a project or work on an existing one.
 */
@Component({
  selector: 'ardea-setup-wizard',
  imports: [
    RouterLink,
    TranslocoPipe,
    UiLoader,
    UiNotice,
    UiPageHeader,
    OperationsStep,
    ProjectStep,
    TechnologiesStep,
    RepositoryStep,
    LinksCard,
    SuggestionsList,
  ],
  template: `
    <ui-page-header
      [heading]="heading() | transloco: { name: project()?.name ?? '' }"
      [sub]="'wizard.lead' | transloco"
    >
      <a uiPageActions class="link" [routerLink]="leaveLink()">{{ 'wizard.later' | transloco }}</a>
    </ui-page-header>

    <nav [attr.aria-label]="'wizard.stepsLabel' | transloco">
      <ol class="stepper">
        @if (mode() === 'organization') {
          <li class="is-done">
            <span class="item"
              ><span>{{ 'wizard.stepNumber' | transloco: { n: 1 } }}</span
              ><b>{{ 'wizard.steps.organization' | transloco }}</b></span
            >
          </li>
        }
        @for (s of steps(); track s; let i = $index) {
          <li [class.is-done]="i < index()">
            @if (i <= furthest() && s !== current()) {
              <a class="item" [routerLink]="[]" [queryParams]="{ step: s }">
                <span>{{ 'wizard.stepNumber' | transloco: { n: i + offset() } }}</span><b>{{
                  stepLabels[s] | transloco
                }}</b>
              </a>
            } @else {
              <span class="item" [attr.aria-current]="s === current() ? 'step' : null">
                <span>{{ 'wizard.stepNumber' | transloco: { n: i + offset() } }}</span><b>{{
                  stepLabels[s] | transloco
                }}</b>
              </span>
            }
          </li>
        }
      </ol>
    </nav>

    <div class="body" [class.with-side]="showSide()">
      @if (orgId(); as org) {
        @if (ready()) {
          @switch (current()) {
            @case ('project') {
              @if (problem()) {
                <ui-notice class="gap" tone="error">{{ problem() | transloco }}</ui-notice>
              }
              <ardea-project-step
                [orgId]="org"
                [project]="project()"
                (saved)="projectSaved($event)"
              />
            }
            @case ('repository') {
              @if (currentProjectId(); as id) {
                <ardea-repository-step
                  [orgId]="org"
                  [projectId]="id"
                  [slug]="current$.slug() ?? ''"
                  [canManage]="manage()"
                  (back)="go(previous())"
                  (continued)="repositoryDone()"
                />
              }
            }
            @case ('operations') {
              @if (project(); as p) {
                <ardea-operations-step
                  [project]="p"
                  [(drafts)]="drafts"
                  [saving]="saving()"
                  [problem]="problem()"
                  (back)="go(previous())"
                  (continued)="saveOperations($event)"
                />
              }
            }
            @case ('technologies') {
              <ardea-technologies-step
                [components]="components()"
                [(drafts)]="drafts"
                [saving]="saving()"
                [problem]="problem()"
                [finishLabel]="mode() === 'project' ? 'wizard.save' : 'wizard.finish'"
                (back)="go(previous())"
                (continued)="saveTechnologies()"
              >
                @if (hasLinks() && currentProjectId(); as id) {
                  <ardea-suggestions-list
                    [orgId]="org"
                    [projectId]="id"
                    [suggestions]="suggestions"
                    [canEdit]="true"
                    (decided)="detail.reload()"
                  />
                }
              </ardea-technologies-step>
            }
          }
          @if (showSide() && currentProjectId(); as id) {
            <aside class="side">
              <ardea-links-card [orgId]="org" [projectId]="id" [links]="links" [compact]="true" />
            </aside>
          }
        } @else if (loadProblem()) {
          <ui-notice tone="error">{{ loadProblem() | transloco }}</ui-notice>
        } @else {
          <ui-loader [label]="'wizard.loading' | transloco" />
        }
      }
    </div>
  `,
  styles: `
    :host {
      display: block;
    }
    .stepper {
      display: grid;
      grid-auto-columns: minmax(0, 1fr);
      grid-auto-flow: column;
      gap: 4px;
      margin-bottom: var(--gap);
    }
    .item {
      position: relative;
      display: grid;
      gap: 2px;
      padding-top: 10px;
      color: var(--faint);
      font-size: var(--fs-small);
      line-height: var(--lh-small);
    }
    .item::before {
      content: '';
      position: absolute;
      inset: 0 0 auto;
      height: 3px;
      border-radius: 2px;
      background: var(--rule);
    }
    .item b {
      color: var(--muted);
      font-size: var(--fs-sub);
      font-weight: 400;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    .is-done .item::before {
      background: var(--muted);
    }
    .is-done .item b {
      color: var(--text);
      font-weight: 500;
    }
    .item[aria-current='step']::before {
      background: var(--text);
    }
    .item[aria-current='step'] b {
      color: var(--text);
      font-weight: 600;
    }
    a.item:focus-visible {
      outline: var(--focus-ring);
      outline-offset: var(--focus-offset);
    }
    .body {
      max-width: 820px;
    }
    .body.with-side {
      display: grid;
      grid-template-columns: minmax(0, 8fr) minmax(0, 4fr);
      gap: var(--gap);
      align-items: start;
      max-width: none;
    }
    @media (max-width: 1100px) {
      .body.with-side {
        grid-template-columns: minmax(0, 1fr);
      }
    }
    .gap {
      display: block;
      margin-bottom: var(--space-lg);
    }
    @media (max-width: 640px) {
      .stepper {
        grid-auto-flow: row;
        grid-template-columns: repeat(2, minmax(0, 1fr));
        row-gap: var(--space-sm);
      }
    }
  `,
})
export class SetupWizard {
  /** From the route's data. */
  readonly mode = input<WizardMode>('organization');
  /** From the address: the project of `/o/:slug/projects/:projectId/setup`. */
  readonly projectId = input<string | null>(null);
  /** From the query string. */
  readonly step = input<string | null>(null);

  private readonly projects = inject(ProjectsService);
  private readonly organizations = inject(OrganizationsService);
  private readonly router = inject(Router);
  protected readonly current$ = injectOrganization();

  protected readonly stepLabels = STEP_LABELS;
  protected readonly orgId = computed(() => this.current$.organization()?.id);
  protected readonly saving = signal(false);
  protected readonly problem = signal<string | null>(null);

  /** Progress of the first run; the other modes do not record any. */
  protected readonly state = getWizardStateResource(
    computed(() => (this.mode() === 'organization' ? this.orgId() : undefined)),
  );

  private readonly createdId = signal<string | null>(null);
  protected readonly currentProjectId = computed(() => {
    if (this.mode() === 'project') return this.projectId();
    if (this.createdId()) return this.createdId();
    if (this.mode() === 'organization' && this.state.hasValue())
      return this.state.value()?.firstProjectId ?? null;
    return null;
  });
  protected readonly detail = getProjectResource(
    this.orgId,
    computed(() => this.currentProjectId() ?? undefined),
  );
  protected readonly project = computed<ProjectView | null>(() =>
    this.detail.hasValue() ? (this.detail.value()?.project ?? null) : null,
  );
  protected readonly components = computed(() =>
    this.detail.hasValue() ? (this.detail.value()?.components ?? []) : [],
  );
  /** The components entered by hand, edited here and saved as a whole (`PUT …/components`). */
  /** The project's repositories: read in the background while Operations is answered. */
  protected readonly links = listRepositoryLinksResource(
    this.orgId,
    computed(() => this.currentProjectId() ?? undefined),
  );
  protected readonly suggestions = listSuggestionsResource(
    this.orgId,
    computed(() => this.currentProjectId() ?? undefined),
    computed(() => ({ state: 'open' })),
  );
  private readonly linkList = computed(() =>
    this.links.hasValue() ? (this.links.value() ?? []) : [],
  );
  protected readonly hasLinks = computed(() => this.linkList().length > 0);
  private readonly reading = computed(() => this.linkList().filter(isReading).length);
  protected readonly showSide = computed(
    () => this.hasLinks() && (this.current() === 'operations' || this.current() === 'technologies'),
  );
  protected readonly manage = computed(() => canManage(this.current$.organization()?.role));

  protected readonly drafts = linkedSignal<DraftComponent[]>(() =>
    this.components()
      .filter((c) => c.origin === 'manual')
      .map(draftFromView),
  );

  protected readonly steps = computed<WizardStep[]>(() =>
    this.mode() === 'project'
      ? ['repository', 'operations', 'technologies']
      : ['project', 'repository', 'operations', 'technologies'],
  );
  protected readonly offset = computed(() => (this.mode() === 'organization' ? 2 : 1));
  protected readonly current = computed<WizardStep>(() => {
    const steps = this.steps();
    const asked = this.step() as WizardStep | null;
    if (asked && steps.includes(asked) && (asked === 'project' || this.currentProjectId()))
      return asked;
    if (!this.currentProjectId()) return steps[0];
    const done = this.state.hasValue() ? (this.state.value()?.stepsDone ?? []) : [];
    return steps.find((s) => !done.includes(s)) ?? steps[steps.length - 1];
  });
  protected readonly index = computed(() => this.steps().indexOf(this.current()));
  /** Steps up to the furthest one reached can be revisited from the stepper. */
  protected readonly furthest = computed(() => {
    if (!this.currentProjectId()) return 0;
    const done = this.state.hasValue() ? (this.state.value()?.stepsDone ?? []) : [];
    const reached = this.steps().map((s, i) => (done.includes(s) ? i + 1 : 0));
    return Math.max(
      this.index(),
      ...reached,
      this.mode() === 'organization' ? 0 : this.steps().length - 1,
    );
  });
  protected readonly previous = computed(() => this.steps()[Math.max(0, this.index() - 1)]);

  protected readonly ready = computed(() => {
    if (this.mode() === 'organization' && !this.state.hasValue()) return false;
    if (this.currentProjectId() && !this.detail.hasValue()) return false;
    return true;
  });
  protected readonly loadProblem = computed(() => {
    const error = this.state.error() ?? this.detail.error();
    return error
      ? problemMessageKey(error, { codes: WIZARD_PROBLEMS, fallback: 'wizard.errors.load' })
      : null;
  });
  protected readonly heading = computed(() =>
    this.mode() === 'organization'
      ? 'wizard.headingOrganization'
      : this.mode() === 'new' && !this.project()
        ? 'wizard.headingNew'
        : 'wizard.headingProject',
  );
  protected readonly leaveLink = computed(() => {
    const slug = this.current$.slug();
    const id = this.currentProjectId();
    return id
      ? ['/o', slug, 'projects', id]
      : ['/o', slug, this.mode() === 'organization' ? 'overview' : 'projects'];
  });

  constructor() {
    // While a repository is read, look again every few seconds; when one finishes, its packages
    // and suggestions are there to show.
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
        });
      }
      before = now;
    });
  }

  protected async repositoryDone(): Promise<void> {
    try {
      await this.record({ stepDone: 'repository', firstProjectId: null });
    } catch {
      // Progress is a convenience; the links exist either way.
    }
    this.links.reload();
    this.go('operations');
  }

  protected go(step: WizardStep): void {
    this.problem.set(null);
    void this.router.navigate([], { queryParams: { step }, queryParamsHandling: 'merge' });
  }

  protected async projectSaved(project: ProjectView): Promise<void> {
    if (this.mode() === 'new') {
      // From here on the project has an address of its own, so a reload continues where it was.
      await this.router.navigate(['/o', this.current$.slug(), 'projects', project.id, 'setup'], {
        queryParams: { step: 'repository' },
      });
      return;
    }
    this.createdId.set(project.id);
    try {
      await this.record({ stepDone: 'project', firstProjectId: project.id });
    } catch (error) {
      this.problem.set(
        problemMessageKey(error, { codes: WIZARD_PROBLEMS, fallback: 'wizard.errors.save' }),
      );
      return;
    }
    this.go('repository');
  }

  protected async saveOperations(answer: OperationsAnswer): Promise<void> {
    await this.save(async (org, id) => {
      await firstValueFrom(
        this.projects.updateProject(org, id, {
          name: null,
          description: null,
          exposure: answer.exposure,
          criticality: answer.criticality,
          archived: null,
        }),
      );
      await this.saveDrafts(org, id);
      await this.record({ stepDone: 'operations', firstProjectId: null });
      this.detail.reload();
      this.go('technologies');
    });
  }

  protected async saveTechnologies(): Promise<void> {
    await this.save(async (org, id) => {
      await this.saveDrafts(org, id);
      if (this.mode() === 'organization') {
        await this.record({ stepDone: 'technologies', firstProjectId: null });
        await firstValueFrom(this.organizations.completeWizard(org));
      }
      await this.router.navigate(['/o', this.current$.slug(), 'projects', id]);
    });
  }

  private async saveDrafts(org: string, id: string): Promise<void> {
    await firstValueFrom(
      this.projects.replaceComponents(org, id, { components: this.drafts().map(toInput) }),
    );
  }

  private async record(change: { stepDone: string; firstProjectId: string | null }): Promise<void> {
    if (this.mode() !== 'organization') return;
    await firstValueFrom(this.organizations.updateWizardState(this.orgId()!, change));
    this.state.reload();
  }

  private async save(work: (org: string, projectId: string) => Promise<void>): Promise<void> {
    const org = this.orgId();
    const id = this.currentProjectId();
    if (!org || !id) return;
    this.saving.set(true);
    this.problem.set(null);
    try {
      await work(org, id);
    } catch (error) {
      this.problem.set(
        problemMessageKey(error, { codes: WIZARD_PROBLEMS, fallback: 'wizard.errors.save' }),
      );
    } finally {
      this.saving.set(false);
    }
  }
}
