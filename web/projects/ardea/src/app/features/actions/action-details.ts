import { Component, computed, inject, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { UiCard, UiInput, UiStatus } from 'ui';
import type { ActionSummary, MemberView } from '../../core/api/client';
import { num, statusKindOf, timeLeft, type Outcome } from './action-format';
import { CLOSING_QUESTIONS, dueDay } from './action-status';
import { Duration } from './duration';
import { ReasonForm } from './reason-form';

/**
 * The facts and settings of an action (mockup remediation.html «Details»): status, owner, deadline,
 * project, component, versions and number. Owner and deadline change in place; a closing asks for
 * its reason here.
 */
@Component({
  selector: 'ardea-action-details',
  imports: [RouterLink, TranslocoPipe, UiCard, UiInput, UiStatus, Duration, ReasonForm],
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
          <dt>
            <label for="action-owner">{{ 'actionDetail.details.owner' | transloco }}</label>
          </dt>
          <dd>
            <select
              uiInput
              id="action-owner"
              [disabled]="!editable() || busy()"
              (change)="assign.emit(value($event) || null)"
            >
              <option value="" [selected]="!a.assignedTo">
                {{ 'actionDetail.details.unassigned' | transloco }}
              </option>
              @for (m of members(); track m.userId) {
                <option [value]="m.userId" [selected]="m.userId === a.assignedTo">
                  {{
                    m.userId === me()
                      ? ('actionDetail.details.you' | transloco: { name: m.displayName })
                      : m.displayName
                  }}
                </option>
              }
            </select>
          </dd>
        </div>
        <div>
          <dt>
            <label for="action-due">{{ 'actionDetail.details.due' | transloco }}</label>
          </dt>
          <dd>
            <input
              uiInput
              type="date"
              id="action-due"
              [value]="day()"
              [disabled]="!editable() || busy()"
              (change)="due.emit(value($event))"
            />
          </dd>
        </div>
        @if (left(); as ms) {
          <div>
            <dt>
              {{
                (ms < 0 ? 'actionDetail.details.overdue' : 'actionDetail.details.left') | transloco
              }}
            </dt>
            <dd><ardea-duration [ms]="ms" /></dd>
          </div>
        }
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
      @if (pending(); as outcome) {
        <ardea-reason-form
          class="reason"
          [label]="question(outcome) | transloco"
          [submitLabel]="'actionDetail.reason.submit' | transloco"
          [busy]="busy()"
          (save)="reason.emit($event)"
          (dismiss)="cancelReason.emit()"
        />
      }
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
    dd select,
    dd input {
      width: auto;
      max-width: 190px;
      font-weight: 500;
    }
    .reason {
      display: block;
      margin-top: var(--space-lg);
    }
  `,
})
export class ActionDetails {
  readonly action = input.required<ActionSummary>();
  readonly members = input<readonly MemberView[]>([]);
  readonly me = input<string | null>(null);
  readonly canEdit = input(false);
  readonly busy = input(false);
  readonly now = input.required<Date>();
  /** The outcome waiting for its reason. */
  readonly pending = input<Outcome | null>(null);

  readonly assign = output<string | null>();
  readonly due = output<string>();
  readonly reason = output<string>();
  readonly cancelReason = output();

  private readonly transloco = inject(TranslocoService);

  /** A closed action keeps its owner and deadline (the API refuses changes). */
  protected readonly editable = computed(() => this.canEdit() && this.action().status !== 'closed');
  protected readonly kind = computed(() =>
    statusKindOf(this.action().status, this.action().closeReason),
  );
  protected readonly number = computed(() => num(this.action().number));
  protected readonly day = computed(() => dueDay(this.action().dueAt));
  protected readonly left = computed(() => {
    const a = this.action();
    return a.dueAt && a.status !== 'closed' ? timeLeft(a.dueAt, this.now()) || 1 : null;
  });
  protected question(outcome: Outcome): string {
    return CLOSING_QUESTIONS[outcome];
  }

  protected value(event: Event): string {
    return (event.target as HTMLInputElement | HTMLSelectElement).value;
  }

  protected when(value: string): string {
    return new Intl.DateTimeFormat(this.transloco.getActiveLang(), {
      dateStyle: 'medium',
      timeStyle: 'short',
    }).format(new Date(value));
  }
}
