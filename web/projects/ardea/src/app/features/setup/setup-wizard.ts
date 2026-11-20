import {
  afterRenderEffect,
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  input,
  linkedSignal,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiLoader, UiNotice, UiPageHeader, UiToaster } from 'ui';
import {
  getProfileCoverageResource,
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
import { StackStep } from './stack-step';
import { TechnologiesStep } from './technologies-step';
import { RepositoryStep } from './repository-step';
import { LinksCard } from '../projects/links-card';
import { CoverageCard } from '../projects/coverage-card';
import { SuggestionsList } from '../projects/suggestions-list';
import { isReading, pollWhile } from '../projects/link-format';
import { canManage } from '../organizations/org-format';
import { WIZARD_PROBLEMS } from './wizard-problems';

export type WizardMode = 'organization' | 'new' | 'project';

export type WizardStep =
  'stack' | 'project' | 'repository' | 'operations' | 'technologies' | 'coverage';

const WITHOUT_PROJECT: readonly WizardStep[] = ['stack', 'project'];

const STEP_LABELS: Record<WizardStep, string> = {
  stack: 'wizard.steps.stack',
  project: 'wizard.steps.project',
  repository: 'wizard.steps.repository',
  operations: 'wizard.steps.operations',
  technologies: 'wizard.steps.technologies',
  coverage: 'wizard.steps.coverage',
};

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
    StackStep,
    TechnologiesStep,
    RepositoryStep,
    CoverageCard,
    UiButton,
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
          <li [class.is-done]="i < index() || done().includes(s)">
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

    <div class="body" [class.with-side]="showSide()" [class.wide]="current() === 'stack'">
      @if (orgId(); as org) {
        @if (ready()) {
          <h2 #stepTitle class="sr-only" tabindex="-1">
            {{ 'wizard.stepNumber' | transloco: { n: index() + offset() } }}:
            {{ stepLabels[current()] | transloco }}
          </h2>
          @switch (current()) {
            @case ('stack') {
              <ardea-stack-step
                [saving]="saving()"
                [problem]="problem()"
                (continued)="stackDone()"
              />
            }
            @case ('project') {
              @if (problem()) {
                <ui-notice class="gap" tone="error">{{ problem() | transloco }}</ui-notice>
              }
              <ardea-project-step
                [orgId]="org"
                [project]="project()"
                [optional]="canSkip()"
                [skipping]="saving()"
                (saved)="projectSaved($event)"
                (back)="go('stack')"
                (skipped)="skipProjects()"
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
                [finishLabel]="'wizard.continue'"
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
            @case ('coverage') {
              <div class="coverage">
                <p class="hint">{{ 'wizard.coverage.lead' | transloco }}</p>
                <ardea-coverage-card [coverage]="coverage" />
                @if (problem()) {
                  <ui-notice tone="error">{{ problem() | transloco }}</ui-notice>
                }
                <div class="actions">
                  <button uiButton variant="ghost" type="button" (click)="go(previous())">
                    {{ 'wizard.back' | transloco }}
                  </button>
                  <button
                    uiButton
                    variant="primary"
                    type="button"
                    [loading]="saving()"
                    (click)="finish()"
                  >
                    {{ (mode() === 'organization' ? 'wizard.finish' : 'wizard.done') | transloco }}
                  </button>
                </div>
              </div>
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
    .body.wide {
      max-width: none;
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
    .coverage {
      display: grid;
      gap: var(--gap);
    }
    .coverage .hint {
      color: var(--muted);
      max-width: 72ch;
    }
    .actions {
      display: flex;
      justify-content: space-between;
      gap: var(--space-md);
      padding-top: var(--space-lg);
      border-top: 1px solid var(--border);
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
  readonly mode = input<WizardMode>('organization');
  readonly projectId = input<string | null>(null);
  readonly step = input<string | null>(null);

  private readonly projects = inject(ProjectsService);
  private readonly organizations = inject(OrganizationsService);
  private readonly router = inject(Router);
  private readonly toaster = inject(UiToaster);
  private readonly transloco = inject(TranslocoService);
  protected readonly current$ = injectOrganization();
  private readonly stepTitle = viewChild<ElementRef<HTMLElement>>('stepTitle');

  protected readonly stepLabels = STEP_LABELS;
  protected readonly orgId = computed(() => this.current$.organization()?.id);
  protected readonly saving = signal(false);
  protected readonly problem = signal<string | null>(null);

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
  protected readonly coverage = getProfileCoverageResource(
    this.orgId,
    computed(() => {
      const projectId = this.currentProjectId();
      return projectId && this.current() === 'coverage' ? { projectId } : undefined;
    }),
  );
  protected readonly manage = computed(() => canManage(this.current$.organization()?.role));

  protected readonly drafts = linkedSignal<DraftComponent[]>(() =>
    this.components()
      .filter((c) => c.origin === 'manual')
      .map(draftFromView),
  );

  protected readonly steps = computed<WizardStep[]>(() => {
    switch (this.mode()) {
      case 'organization':
        return ['stack', 'project', 'repository', 'operations', 'technologies', 'coverage'];
      case 'new':
        return ['project', 'repository', 'operations', 'technologies', 'coverage'];
      default:
        return ['repository', 'operations', 'technologies', 'coverage'];
    }
  });
  protected readonly offset = computed(() => (this.mode() === 'organization' ? 2 : 1));
  protected readonly done = computed(() =>
    this.state.hasValue() ? (this.state.value()?.stepsDone ?? []) : [],
  );
  protected readonly canSkip = computed(
    () => this.mode() === 'organization' && this.done().includes('stack'),
  );
  private readonly open = (step: WizardStep): boolean =>
    WITHOUT_PROJECT.includes(step) || !!this.currentProjectId();
  protected readonly current = computed<WizardStep>(() => {
    const steps = this.steps();
    const asked = this.step() as WizardStep | null;
    if (asked && steps.includes(asked) && this.open(asked)) return asked;
    const next = steps.find((s) => !this.done().includes(s)) ?? steps[steps.length - 1];
    return this.open(next) ? next : (steps.find((s) => s === 'project') ?? steps[0]);
  });
  protected readonly index = computed(() => this.steps().indexOf(this.current()));
  protected readonly furthest = computed(() => {
    const steps = this.steps();
    const reached = steps.map((s, i) => (this.done().includes(s) ? i + 1 : 0));
    const furthest = Math.max(
      this.index(),
      ...reached,
      this.mode() === 'organization' ? 0 : steps.length - 1,
    );
    return this.currentProjectId() ? furthest : Math.min(furthest, steps.indexOf('project'));
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
    let shown: WizardStep | null = null;
    afterRenderEffect(() => {
      const title = this.stepTitle()?.nativeElement;
      if (!title) return;
      const step = this.current();
      if (shown !== null && shown !== step) title.focus();
      shown = step;
    });
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
    await this.record({ stepDone: 'repository', firstProjectId: null }).catch(() => undefined);
    this.links.reload();
    this.go('operations');
  }

  protected async stackDone(): Promise<void> {
    this.saving.set(true);
    this.problem.set(null);
    try {
      const done = await this.record({ stepDone: 'stack', firstProjectId: null });
      const next = this.steps().find((s) => s !== 'stack' && !done.includes(s)) ?? 'project';
      this.go(this.open(next) ? next : 'project');
    } catch (error) {
      this.problem.set(
        problemMessageKey(error, { codes: WIZARD_PROBLEMS, fallback: 'wizard.errors.save' }),
      );
    } finally {
      this.saving.set(false);
    }
  }

  protected async skipProjects(): Promise<void> {
    const org = this.orgId();
    if (!org || this.saving()) return;
    if (!this.canSkip()) {
      this.go('stack');
      return;
    }
    this.saving.set(true);
    this.problem.set(null);
    try {
      await firstValueFrom(this.organizations.completeWizard(org));
      this.toaster.show({ text: this.transloco.translate('wizard.finishedWithStack') });
      await this.router.navigate(['/o', this.current$.slug(), 'overview']);
    } catch (error) {
      this.problem.set(
        problemMessageKey(error, { codes: WIZARD_PROBLEMS, fallback: 'wizard.errors.save' }),
      );
    } finally {
      this.saving.set(false);
    }
  }

  protected go(step: WizardStep): void {
    this.problem.set(null);
    void this.router.navigate([], { queryParams: { step }, queryParamsHandling: 'merge' });
  }

  protected async projectSaved(project: ProjectView): Promise<void> {
    if (this.mode() === 'new') {
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
      await this.record({ stepDone: 'technologies', firstProjectId: null });
      this.detail.reload();
      this.go('coverage');
    });
  }

  protected async finish(): Promise<void> {
    await this.save(async (org, id) => {
      if (this.mode() === 'organization') {
        await this.record({ stepDone: 'coverage', firstProjectId: null });
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

  private async record(change: {
    stepDone: string;
    firstProjectId: string | null;
  }): Promise<readonly string[]> {
    if (this.mode() !== 'organization') return [];
    const view = await firstValueFrom(this.organizations.updateWizardState(this.orgId()!, change));
    this.state.reload();
    return view.stepsDone;
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
