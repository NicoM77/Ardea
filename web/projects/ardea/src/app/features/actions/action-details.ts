import { Component, inject, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { UiCard } from 'ui';
import type { ActionSummary } from '../../core/api/client';
import { Dates } from '../../core/format/dates';

@Component({
  selector: 'ardea-action-details',
  imports: [TranslocoPipe, UiCard],
  template: `
    @let a = action();
    <ui-card>
      <h2 class="sr-only">{{ 'actionDetail.details.title' | transloco }}</h2>
      <dl class="props">
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
  private readonly dates = inject(Dates);

  protected when(value: string): string {
    return this.dates.dateTime(value);
  }
}
