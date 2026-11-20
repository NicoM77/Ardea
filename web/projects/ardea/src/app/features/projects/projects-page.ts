import { Component, computed, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { LucideFolder, LucidePlus } from '@lucide/angular';
import { UiButton, UiCard, UiEmptyState, UiLoader, UiNotice, UiPageHeader, UiPill } from 'ui';
import { listProjectsResource, type ListProjectsParams } from '../../core/api/client';
import { count } from '../sources/source-format';
import { injectOrganization } from '../organizations/current-organization';
import { roleRank } from '../organizations/org-format';
import { CRITICALITY_LABELS, EXPOSURE_LABELS } from './component-format';

@Component({
  selector: 'ardea-projects-page',
  imports: [
    RouterLink,
    TranslocoPipe,
    LucideFolder,
    LucidePlus,
    UiButton,
    UiCard,
    UiEmptyState,
    UiLoader,
    UiNotice,
    UiPageHeader,
    UiPill,
  ],
  template: `
    <ui-page-header [heading]="'nav.projects' | transloco" [sub]="'projects.lead' | transloco">
      @if (canEdit()) {
        <a uiPageActions uiButton routerLink="new">
          <svg lucidePlus aria-hidden="true"></svg>
          {{ 'projects.new' | transloco }}
        </a>
      }
    </ui-page-header>

    <label class="toggle">
      <input type="checkbox" [checked]="archived()" (change)="archived.set(!archived())" />
      {{ 'projects.showArchived' | transloco }}
    </label>

    <section [attr.aria-busy]="projects.isLoading()">
      @if (projects.hasValue() && projects.value(); as list) {
        @if (list.length) {
          <ul class="list">
            @for (project of list; track project.id) {
              <li>
                <ui-card>
                  <div class="row">
                    <div class="what">
                      <a class="title" [routerLink]="[project.id]">{{ project.name }}</a>
                      @if (project.description) {
                        <p class="sub">{{ project.description }}</p>
                      }
                    </div>
                    <div class="facts">
                      @if (project.archived) {
                        <ui-pill>{{ 'projects.archived' | transloco }}</ui-pill>
                      }
                      <ui-pill>{{ exposureLabels[project.exposure] | transloco }}</ui-pill>
                      <ui-pill>{{ criticalityLabels[project.criticality] | transloco }}</ui-pill>
                      <span class="sub">{{
                        'projects.componentCount'
                          | transloco: { count: number(project.componentCount) }
                      }}</span>
                    </div>
                  </div>
                </ui-card>
              </li>
            }
          </ul>
        } @else {
          <ui-empty-state [heading]="'projects.emptyHeading' | transloco">
            <svg uiEmptyIcon lucideFolder aria-hidden="true"></svg>
            {{ 'projects.emptyText' | transloco }}
            @if (canEdit()) {
              <a uiEmptyActions uiButton variant="primary" routerLink="../setup">
                {{ 'projects.setUp' | transloco }}
              </a>
            }
          </ui-empty-state>
        }
      } @else if (projects.error()) {
        <ui-notice tone="error">{{ 'projects.loadFailed' | transloco }}</ui-notice>
      } @else {
        <ui-loader [label]="'projects.loading' | transloco" />
      }
    </section>
  `,
  styles: `
    :host {
      display: block;
    }
    .toggle {
      display: inline-flex;
      align-items: center;
      gap: var(--space-sm);
      margin-bottom: var(--space-lg);
      color: var(--muted);
      cursor: pointer;
    }
    .toggle input {
      width: 16px;
      height: 16px;
      margin: 0;
      accent-color: var(--text);
    }
    .toggle input:focus-visible {
      outline: var(--focus-ring);
      outline-offset: var(--focus-offset);
    }
    .list {
      display: grid;
      gap: var(--space-md);
    }
    .row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-sm) var(--gap);
    }
    .what {
      min-width: 0;
    }
    .title {
      font-size: var(--fs-section);
      font-weight: 600;
    }
    .title:focus-visible {
      outline: var(--focus-ring);
      outline-offset: var(--focus-offset);
    }
    .facts {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-sm);
    }
    .sub {
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    @media (hover: hover) and (pointer: fine) {
      .title:hover {
        text-decoration: underline;
        text-underline-offset: 3px;
      }
    }
  `,
})
export class ProjectsPage {
  private readonly org = injectOrganization().organization;
  protected readonly exposureLabels = EXPOSURE_LABELS;
  protected readonly criticalityLabels = CRITICALITY_LABELS;
  protected readonly number = count;
  protected readonly archived = signal(false);
  protected readonly canEdit = computed(() => roleRank(this.org()?.role) >= roleRank('member'));
  private readonly orgId = computed(() => this.org()?.id);
  private readonly params = computed<ListProjectsParams>(() =>
    this.archived() ? { archived: true } : {},
  );
  protected readonly projects = listProjectsResource(this.orgId, this.params);
}
