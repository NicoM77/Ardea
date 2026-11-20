import { Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { UiCard, UiStatus } from 'ui';
import type { ActionSummary } from '../../core/api/client';
import { num, statusKindOf } from './action-format';

@Component({
  selector: 'ardea-action-details',
  imports: [RouterLink, TranslocoPipe, UiCard, UiStatus],
  template: `
    @let a = action();
    <ui-card>
      <h2 class="sr-only">{{ 'actionDetail.details.title' | transloco }}</h2>
      <dl class="props">
        <div>
          <dt>{{ 'actionDetail.details.status' | transloco }}</dt>
          <dd><ui-status [kind]="kind()" /></dd>
        </div>
        <div>
          <dt>{{ 'actionDetail.details.project' | transloco }}</dt>
          <dd>
            <a class="link" [routerLink]="['../../projects', a.project.id]">{{ a.project.name }}</a>
          </dd>
        </div>
        <div>
          <dt>{{ 'actionDetail.details.component' | transloco }}</dt>
          <dd>
            {{ a.component.name }}
            @if (a.component.removed) {
              <small>{{ 'actionDetail.details.removed' | transloco }}</small>
            }
          </dd>
        </div>
        <div>
          <dt>{{ 'actionDetail.details.version' | transloco }}</dt>
          <dd>
            <span class="id">{{ a.component.version ?? '–' }}</span>
            @if (a.targetVersion) {
              <span aria-hidden="true"> → </span>
              <span class="sr-only">{{ 'actionDetail.details.to' | transloco }}</span>
              <span class="id">{{ a.targetVersion }}</span>
            }
          </dd>
        </div>
        <div>
          <dt>{{ 'actionDetail.details.number' | transloco }}</dt>
          <dd class="id">#{{ number() }}</dd>
        </div>
        <div>
          <dt>{{ 'actionDetail.details.opened' | transloco }}</dt>
          <dd>{{ when(a.openedAt) }}</dd>
        </div>
      </dl>
    </ui-card>
  `,
  styles: `
    .props > div {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-lg);
      min-height: 40px;
      padding: 6px 0;
    }
    .props > div + div {
      border-top: 1px solid var(--rule);
    }
    dt {
      color: var(--muted);
      white-space: nowrap;
    }
    dd {
      min-width: 0;
      font-weight: 500;
      text-align: right;
      overflow-wrap: anywhere;
    }
    dd small {
      display: block;
      color: var(--muted);
      font-weight: 400;
    }
  `,
})
export class ActionDetails {
  readonly action = input.required<ActionSummary>();
  private readonly transloco = inject(TranslocoService);

  protected readonly kind = computed(() =>
    statusKindOf(this.action().status, this.action().closeReason),
  );
  protected readonly number = computed(() => num(this.action().number));

  protected when(value: string): string {
    return new Intl.DateTimeFormat(this.transloco.getActiveLang(), {
      dateStyle: 'medium',
      timeStyle: 'short',
    }).format(new Date(value));
  }
}
