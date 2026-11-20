import { Component, input, output, signal, viewChildren } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { LucideChevronRight } from '@lucide/angular';
import { UiInput, UiPill, UiPriority, UiStatus } from 'ui';
import type { FindingView, MemberView } from '../../core/api/client';
import {
  isOutcome,
  num,
  priorityOf,
  statusKindOf,
  SYSTEM_REASON_LABELS,
  type Outcome,
} from './action-format';
import { checkLabel } from './answers-format';
import { FactorTable } from './factor-table';
import { LearnedNote } from './learned-note';
import { hasModelEstimate, isProposal } from './model-format';
import { ModelReview, type ReviewChange } from './model-review';
import { ReasonForm } from './reason-form';

export interface FindingChange {
  finding: FindingView;
  status: Outcome | 'open';
  note: string | null;
}

@Component({
  selector: 'ardea-findings-list',
  imports: [
    RouterLink,
    TranslocoPipe,
    LucideChevronRight,
    UiInput,
    UiPill,
    UiPriority,
    UiStatus,
    FactorTable,
    LearnedNote,
    ModelReview,
    ReasonForm,
  ],
  template: `
    @for (f of findings(); track f.id; let first = $first) {
      <details class="finding" [open]="(first && openFirst()) || waiting(f)">
        <summary>
          <ui-priority [level]="priority(f.levelName)" />
          <span class="title">
            <b
              ><span class="id">{{ f.entry.key }}</span> · {{ f.entry.title ?? '' }}</b
            >
            <small>
              {{ 'findings.verdict' | transloco: { verdict: f.verdict } }} ·
              @if (f.fixedVersion) {
                {{ 'findings.fixedIn' | transloco: { version: f.fixedVersion } }}
              } @else {
                {{ 'findings.noFix' | transloco }}
              }
            </small>
          </span>
          <span class="tags">
            <ui-pill>{{ check(f.versionVerdict) | transloco }}</ui-pill>
            @if (proposal(f)) {
              <span class="ai-tag">{{ 'estimate.proposal' | transloco }}</span>
            } @else if (f.verdictSource === 'model') {
              <span class="ai-tag">{{
                'answers.model.verdict' | transloco: { verdict: f.verdict }
              }}</span>
            }
            @if (f.status !== 'open') {
              <ui-status [kind]="kind(f)" />
            }
          </span>
          <svg lucideChevronRight class="chevron" aria-hidden="true"></svg>
        </summary>
        <div class="body">
          <p class="facts">
            <span
              >{{ 'findings.installed' | transloco }}
              <b class="id">{{
                f.component.version ?? ('findings.unknownVersion' | transloco)
              }}</b></span
            >
            <span
              >{{ 'findings.source' | transloco }}
              <b>{{ 'findings.verdictSource' | transloco: { source: f.verdictSource } }}</b></span
            >
            @if (reason(f); as why) {
              <span>{{ why | transloco }}</span>
            }
            @if (f.statusNote) {
              <span
                >{{ 'findings.note' | transloco }} <q>{{ f.statusNote }}</q></span
              >
            }
          </p>
          <ardea-learned-note [evidence]="f.evidence" />
          <ardea-factor-table
            [factors]="f.factors"
            [caption]="'findings.factorsOf' | transloco: { entry: f.entry.key }"
          />
          <ardea-model-review
            [finding]="f"
            [members]="members()"
            [canEdit]="canEdit()"
            [busy]="busy()"
            (review)="review.emit($event)"
          />
          <div class="actions">
            @if (f.alertNumber !== null) {
              <a class="link" [routerLink]="['../../alerts', alertNumber(f)]">
                {{ 'findings.alert' | transloco: { number: alertNumber(f) } }}
              </a>
            }
            @if (canEdit()) {
              <label class="status">
                <span class="sr-only">{{
                  'findings.statusOf' | transloco: { entry: f.entry.key }
                }}</span>
                <select uiInput [disabled]="busy()" (change)="choose(f, $event)">
                  <option value="" selected>{{ 'findings.change' | transloco }}</option>
                  @if (isOpen(f)) {
                    <option value="remediated">{{ 'ui.status.remediated' | transloco }}</option>
                    <option value="risk_accepted">{{ 'ui.status.accepted' | transloco }}</option>
                    <option value="not_affected">{{ 'ui.status.notAffected' | transloco }}</option>
                  } @else {
                    <option value="open">{{ 'findings.reopen' | transloco }}</option>
                  }
                </select>
              </label>
            }
          </div>
          @if (pending()?.id === f.id) {
            <ardea-reason-form
              [label]="'findings.reasonLabel' | transloco: { entry: f.entry.key }"
              [submitLabel]="'findings.reasonSave' | transloco"
              [busy]="busy()"
              (save)="outcome.emit({ finding: f, status: pending()!.status, note: $event })"
              (dismiss)="pending.set(null)"
            />
          }
        </div>
      </details>
    }
  `,
  styles: `
    .finding + .finding {
      border-top: 1px solid var(--rule);
    }
    summary {
      display: grid;
      grid-template-columns: 104px minmax(0, 1fr) auto 16px;
      align-items: center;
      gap: var(--space-xs) var(--space-lg);
      min-height: 56px;
      margin: 0 calc(-1 * var(--space-sm));
      padding: var(--space-sm);
      border-radius: var(--radius-control);
      cursor: pointer;
      list-style: none;
    }
    summary::-webkit-details-marker {
      display: none;
    }
    .chevron {
      color: var(--faint);
      transition: transform var(--t-ui) var(--ease-out);
    }
    details[open] .chevron {
      transform: rotate(90deg);
    }
    .title b {
      display: block;
      font-weight: 500;
      overflow-wrap: anywhere;
    }
    .title small,
    .facts {
      display: block;
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .ai-tag {
      display: inline-flex;
      align-items: center;
      height: 20px;
      padding: 0 6px;
      border-radius: 4px;
      background: var(--accent-soft);
      color: var(--text);
      font-size: var(--fs-small);
      font-weight: 500;
      white-space: nowrap;
    }
    ardea-model-review:empty {
      display: none;
    }
    .tags {
      display: flex;
      flex-wrap: wrap;
      justify-content: flex-end;
      gap: 6px;
    }
    .body {
      display: grid;
      gap: var(--space-md);
      padding: var(--space-xs) 0 var(--space-lg) 120px;
    }
    .facts {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-xs) var(--space-xl);
    }
    .facts b {
      color: var(--text);
      font-weight: 500;
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-sm) var(--space-lg);
    }
    .status select {
      width: auto;
    }
    @media (hover: hover) and (pointer: fine) {
      summary:hover {
        background: var(--raised);
      }
    }
    @media (max-width: 640px) {
      summary {
        grid-template-columns: minmax(0, 1fr) 16px;
      }
      summary > :not(.chevron) {
        grid-column: 1;
      }
      .tags {
        justify-content: flex-start;
      }
      .chevron {
        grid-column: 2;
        grid-row: 1 / span 3;
      }
      .body {
        padding-left: 0;
      }
    }
  `,
})
export class FindingsList {
  readonly findings = input.required<readonly FindingView[]>();
  readonly canEdit = input(false);
  readonly busy = input(false);
  readonly members = input<readonly MemberView[]>([]);
  readonly openFirst = input(true);
  readonly outcome = output<FindingChange>();
  readonly review = output<ReviewChange>();

  private readonly reviews = viewChildren(ModelReview);

  protected readonly pending = signal<{ id: string; status: Outcome } | null>(null);

  protected readonly priority = priorityOf;
  protected readonly check = checkLabel;
  protected readonly waiting = hasModelEstimate;
  protected readonly proposal = isProposal;

  protected kind(f: FindingView) {
    return statusKindOf(f.status, f.statusReason);
  }

  protected isOpen(f: FindingView): boolean {
    return f.status === 'open';
  }

  protected reason(f: FindingView): string | null {
    return f.statusReason ? (SYSTEM_REASON_LABELS[f.statusReason] ?? null) : null;
  }

  protected alertNumber(f: FindingView): number {
    return num(f.alertNumber);
  }

  protected choose(f: FindingView, event: Event): void {
    const select = event.target as HTMLSelectElement;
    const status = select.value;
    select.value = '';
    if (status === 'open') {
      this.pending.set(null);
      this.outcome.emit({ finding: f, status: 'open', note: null });
    } else if (isOutcome(status)) {
      this.pending.set({ id: f.id, status });
    }
  }

  done(): void {
    this.pending.set(null);
    for (const review of this.reviews()) review.done();
  }
}
