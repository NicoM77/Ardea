import { Component, ElementRef, inject, input, linkedSignal, output, signal } from '@angular/core';
import { form, FormField, FormRoot, maxLength, required } from '@angular/forms/signals';
import type { TreeValidationResult } from '@angular/forms/signals';
import { TranslocoPipe } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiCard, UiField, UiInput, UiNotice } from 'ui';
import { ProjectsService, type ProjectView } from '../../core/api/client';
import { problemMessageKey } from '../../core/api/problem';
import { fieldProblems } from '../admin/admin-errors';
import { fieldErrorKey } from '../auth/auth-forms';
import { focusFirstInvalid } from '../auth/focus-invalid';
import { WIZARD_PROBLEMS } from './wizard-problems';

/**
 * Step "Project" (master plan 11.6): the name and the business process it carries. Exposure and
 * criticality follow in Operations, while the repository is read. An existing project is renamed,
 * not created again: the wizard never asks twice.
 */
@Component({
  selector: 'ardea-project-step',
  imports: [FormField, FormRoot, TranslocoPipe, UiButton, UiCard, UiField, UiInput, UiNotice],
  template: `
    <form class="form" [formRoot]="projectForm">
      <ui-card [heading]="'wizard.project.title' | transloco">
        <div class="fields">
          <p class="hint">{{ 'wizard.project.lead' | transloco }}</p>
          @if (problem()) {
            <ui-notice tone="error">{{ problem() | transloco }}</ui-notice>
          }
          <ui-field
            [label]="'wizard.project.name' | transloco"
            [hint]="'wizard.project.nameHint' | transloco"
            [error]="fieldErrorKey(projectForm.name, nameErrors) | transloco"
          >
            <input uiInput type="text" autocomplete="off" [formField]="projectForm.name" />
          </ui-field>
          <ui-field
            [label]="'wizard.project.description' | transloco"
            [hint]="'wizard.project.descriptionHint' | transloco"
            [error]="fieldErrorKey(projectForm.description, descriptionErrors) | transloco"
          >
            <textarea uiInput rows="3" [formField]="projectForm.description"></textarea>
          </ui-field>
        </div>
      </ui-card>
      <div class="actions">
        <span></span>
        <button uiButton variant="primary" type="submit" [loading]="projectForm().submitting()">
          {{ 'wizard.continue' | transloco }}
        </button>
      </div>
    </form>
  `,
  styles: `
    :host {
      display: block;
    }
    .form {
      display: grid;
      gap: var(--gap);
    }
    .fields {
      display: grid;
      gap: var(--space-lg);
    }
    .hint {
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
  `,
})
export class ProjectStep {
  readonly orgId = input.required<string>();
  /** The project the wizard already created, if it comes back to this step. */
  readonly project = input<ProjectView | null>(null);
  readonly saved = output<ProjectView>();

  private readonly projects = inject(ProjectsService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  protected readonly fieldErrorKey = fieldErrorKey;
  protected readonly nameErrors = {
    required: 'wizard.project.errors.name',
    maxLength: 'wizard.project.errors.nameTooLong',
    server: 'wizard.project.errors.name',
  };
  protected readonly descriptionErrors = {
    maxLength: 'wizard.project.errors.descriptionTooLong',
    server: 'wizard.project.errors.descriptionTooLong',
  };
  protected readonly problem = signal<string | null>(null);

  private readonly model = linkedSignal(() => ({
    name: this.project()?.name ?? '',
    description: this.project()?.description ?? '',
  }));

  protected readonly projectForm = form(
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
          const { name, description } = this.model();
          const existing = this.project();
          try {
            const project = existing
              ? await firstValueFrom(
                  this.projects.updateProject(this.orgId(), existing.id, {
                    name: name.trim(),
                    description: description.trim(),
                    criticality: null,
                    exposure: null,
                    archived: null,
                  }),
                )
              : await firstValueFrom(
                  this.projects.createProject(this.orgId(), {
                    name: name.trim(),
                    description: description.trim() || null,
                    criticality: null,
                    exposure: null,
                  }),
                );
            this.saved.emit(project);
            return undefined;
          } catch (error) {
            const onField = fieldProblems(error, {
              name: this.projectForm.name,
              description: this.projectForm.description,
            });
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
}
