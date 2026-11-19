import { Component, computed, ElementRef, inject, signal } from '@angular/core';
import {
  form,
  FormField,
  FormRoot,
  maxLength,
  pattern,
  required,
  type TreeValidationResult,
} from '@angular/forms/signals';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { UiButton, UiCard, UiField, UiInput, UiNotice, UiPageHeader } from 'ui';
import { OrganizationsService } from '../../core/api/client';
import { problemMessageKey, problemOf } from '../../core/api/problem';
import { Session } from '../../core/session/session';
import { fieldProblems } from '../admin/admin-errors';
import { fieldErrorKey } from '../auth/auth-forms';
import { focusFirstInvalid } from '../auth/focus-invalid';
import { SLUG_MAX_LENGTH, SLUG_PATTERN, slugify } from './org-format';

/**
 * Founding an organization (master plan 9.1): any signed-in person may, and becomes its owner.
 * The address follows the name until someone types their own.
 */
@Component({
  selector: 'ardea-create-organization-page',
  imports: [
    FormField,
    FormRoot,
    RouterLink,
    TranslocoPipe,
    UiButton,
    UiCard,
    UiField,
    UiInput,
    UiNotice,
    UiPageHeader,
  ],
  template: `
    <ui-page-header
      [heading]="'organizations.create.title' | transloco"
      [sub]="'organizations.create.lead' | transloco"
    />
    <ui-card class="card">
      <form class="form" [formRoot]="create">
        @if (problem()) {
          <ui-notice tone="error">{{ problem() | transloco }}</ui-notice>
        }
        <ui-field
          [label]="'organizations.create.name' | transloco"
          [hint]="'organizations.create.nameHint' | transloco"
          [error]="fieldErrorKey(create.name, nameErrors) | transloco"
        >
          <input uiInput type="text" autocomplete="organization" [formField]="create.name" />
        </ui-field>
        <ui-field
          [label]="'organizations.create.slug' | transloco"
          [hint]="'organizations.create.slugHint' | transloco: { path: preview() }"
          [error]="fieldErrorKey(create.slug, slugErrors) | transloco"
        >
          <input
            uiInput
            mono
            type="text"
            autocomplete="off"
            spellcheck="false"
            [placeholder]="derived()"
            [formField]="create.slug"
          />
        </ui-field>
        <div class="actions">
          <button uiButton variant="primary" type="submit" [loading]="create().submitting()">
            {{ 'organizations.create.submit' | transloco }}
          </button>
          <a uiButton variant="ghost" routerLink="/">{{
            'organizations.create.cancel' | transloco
          }}</a>
        </div>
      </form>
    </ui-card>
  `,
  styles: `
    :host {
      display: block;
    }
    .card {
      max-width: 560px;
    }
    .form {
      display: grid;
      gap: var(--space-lg);
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-sm);
    }
  `,
})
export class CreateOrganizationPage {
  private readonly organizations = inject(OrganizationsService);
  private readonly session = inject(Session);
  private readonly router = inject(Router);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  protected readonly fieldErrorKey = fieldErrorKey;
  protected readonly nameErrors = {
    required: 'organizations.create.errors.nameRequired',
    maxLength: 'organizations.create.errors.nameTooLong',
    server: 'organizations.create.errors.nameRequired',
  };
  protected readonly slugErrors = {
    pattern: 'organizations.create.errors.slugFormat',
    server: 'organizations.create.errors.slugFormat',
    slugTaken: 'organizations.create.errors.slugTaken',
  };
  protected readonly problem = signal<string | null>(null);

  private readonly model = signal({ name: '', slug: '' });
  /** The address the API derives from the name when the field is left empty. */
  protected readonly derived = computed(() => slugify(this.model().name));
  protected readonly preview = computed(() => `/o/${this.model().slug.trim() || this.derived()}`);

  protected readonly create = form(
    this.model,
    (p) => {
      required(p.name);
      maxLength(p.name, 200);
      maxLength(p.slug, SLUG_MAX_LENGTH);
      pattern(p.slug, SLUG_PATTERN);
    },
    {
      submission: {
        action: async (): Promise<TreeValidationResult> => {
          this.problem.set(null);
          const { name, slug } = this.model();
          try {
            const created = await firstValueFrom(
              this.organizations.createOrganization({
                name: name.trim(),
                slug: slug.trim() || null,
              }),
            );
            await this.session.refresh();
            await this.router.navigate(['/o', created.slug, 'team']);
            return undefined;
          } catch (error) {
            if (problemOf(error)?.code === 'slug_taken') {
              focusFirstInvalid(this.host);
              return [{ kind: 'slugTaken', fieldTree: this.create.slug }];
            }
            const onField = fieldProblems(error, {
              name: this.create.name,
              slug: this.create.slug,
            });
            if (onField) {
              focusFirstInvalid(this.host);
              return onField;
            }
            this.problem.set(
              problemMessageKey(error, { fallback: 'organizations.create.errors.failed' }),
            );
            return undefined;
          }
        },
        onInvalid: () => focusFirstInvalid(this.host),
      },
    },
  );
}
