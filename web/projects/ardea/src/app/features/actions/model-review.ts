import {
  Component,
  computed,
  ElementRef,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { UiButton, UiInput, UiPill, UiPriority } from 'ui';
import type { FindingView, MemberView } from '../../core/api/client';
import { num, priorityOfLevel } from './action-format';
import {
  EXPECTED_LEVELS,
  hasModelEstimate,
  heldByFloor,
  isProposal,
  levelWithoutModel,
  reviewer,
  VERDICTS,
} from './model-format';

/** The level a person may expect, in the words of the priorities. */
const LEVEL_NAMES: Readonly<Record<number, string>> = {
  5: 'estimate.levels.critical',
  4: 'estimate.levels.high',
  3: 'estimate.levels.medium',
  2: 'estimate.levels.low',
  1: 'estimate.levels.notRelevant',
};

/** What a person decided about a finding's assessment (task 4.7). */
export interface ReviewChange {
  finding: FindingView;
  decision: 'confirm' | 'correct';
  /** The person's verdict; null when only the level is questioned, or for a confirmation. */
  verdict: string | null;
  /** The level the person expects instead; null when the level is not questioned. */
  level: number | null;
  reason: string | null;
}

/**
 * The model's estimate of one finding and what a person does with it (mockup remediation.html?ai,
 * master plan 7.5, 7.6, 7.9; tasks 4.5, 4.7): the verdict, marked as a proposal when it is «not
 * affected»; the level the rules give without it and with it; «Confirm the estimate», which makes
 * it a person's verdict (a confirmed «not affected» closes the finding); and «Correct this
 * assessment» with another verdict, the level the person expects, and why. Once reviewed, it says
 * who confirmed or corrected it.
 */
@Component({
  selector: 'ardea-model-review',
  imports: [TranslocoPipe, UiButton, UiInput, UiPill, UiPriority],
  template: `
    @let f = finding();
    @if (shown()) {
      <div class="model">
        @if (estimate()) {
          <p class="head">
            <span class="ai-tag">{{ 'estimate.estimate' | transloco }}</span>
            <b>{{ 'findings.verdict' | transloco: { verdict: f.verdict } }}</b>
            @if (proposal()) {
              <ui-pill>{{ 'estimate.proposal' | transloco }}</ui-pill>
            }
          </p>
          @if (from(); as before) {
            <p class="head">
              <ui-priority [level]="before" />
              <span class="arrow" aria-hidden="true">→</span>
              <span class="sr-only">{{ 'estimate.withModel' | transloco }}</span>
              <ui-priority [level]="now()" />
              <span class="muted">{{
                (held() ? 'estimate.held' : 'estimate.counts') | transloco
              }}</span>
            </p>
          }
          @if (proposal()) {
            <p class="muted">{{ 'estimate.proposalNote' | transloco }}</p>
          }
          @if (levelOnly(); as r) {
            <p class="muted">
              {{
                'estimate.expected'
                  | transloco: { who: who(), level: expectedName(r.expectedLevel) }
              }}
              @if (r.reason) {
                <q>{{ r.reason }}</q>
              }
            </p>
          }
          @if (canEdit()) {
            <div class="buttons">
              <button uiButton type="button" [loading]="busy() && !open()" (click)="confirm()">
                {{ 'estimate.confirm' | transloco }}
              </button>
              <button
                uiButton
                variant="ghost"
                type="button"
                [attr.aria-expanded]="open()"
                [attr.aria-controls]="id + '-form'"
                (click)="toggle()"
              >
                {{ 'estimate.correct' | transloco }}
              </button>
            </div>
          }
        } @else if (f.review; as r) {
          @if (r.decision === 'confirmed') {
            <p class="head">
              <span class="ai-tag">{{ 'estimate.model' | transloco }}</span>
              <b>{{ 'findings.verdict' | transloco: { verdict: f.verdict } }}</b>
              <span class="muted">{{ 'estimate.confirmedBy' | transloco: { who: who() } }}</span>
            </p>
          } @else {
            <p class="head">
              <span class="ai-tag">{{ 'estimate.corrected' | transloco }}</span>
              <b>{{ 'findings.verdict' | transloco: { verdict: f.verdict } }}</b>
              <span class="muted">{{ 'estimate.correctedBy' | transloco: { who: who() } }}</span>
              @if (r.reason) {
                <q>{{ r.reason }}</q>
              }
            </p>
            <p class="muted">{{ 'estimate.reassessed' | transloco }}</p>
          }
          @if (canEdit() && open() === false) {
            <div class="buttons">
              <button
                uiButton
                variant="ghost"
                type="button"
                [attr.aria-expanded]="open()"
                [attr.aria-controls]="id + '-form'"
                (click)="toggle()"
              >
                {{ 'estimate.correctAgain' | transloco }}
              </button>
            </div>
          }
        }

        @if (open()) {
          <form class="form" novalidate [id]="id + '-form'" (submit)="submit($event)">
            <div class="row">
              <label class="field">
                <span>{{ 'estimate.verdict' | transloco }}</span>
                <select uiInput #verdict>
                  @for (v of verdicts; track v) {
                    <option [value]="v" [selected]="v === initialVerdict()">
                      {{ 'findings.verdict' | transloco: { verdict: v } }}
                    </option>
                  }
                </select>
              </label>
              <label class="field">
                <span>{{ 'estimate.level' | transloco }}</span>
                <select uiInput #level>
                  <option value="" selected>{{ 'estimate.levelRight' | transloco }}</option>
                  @for (l of levels; track l) {
                    <option [value]="l">{{ expectedName(l) | transloco }}</option>
                  }
                </select>
              </label>
            </div>
            <label class="field">
              <span>{{ 'estimate.why' | transloco }}</span>
              <textarea
                uiInput
                #why
                rows="2"
                maxlength="2000"
                [attr.aria-invalid]="error() ? true : null"
                [attr.aria-describedby]="error() ? id + '-error' : null"
                (input)="error.set(null)"
              ></textarea>
            </label>
            @if (error(); as e) {
              <p class="error" [id]="id + '-error'">{{ e | transloco }}</p>
            }
            <div class="buttons">
              <button uiButton type="button" (click)="open.set(false)">
                {{ 'actionDetail.reason.cancel' | transloco }}
              </button>
              <button uiButton variant="primary" type="submit" [loading]="busy()">
                {{ 'estimate.save' | transloco }}
              </button>
            </div>
          </form>
        }
      </div>
    }
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }
    .model {
      display: grid;
      gap: var(--space-sm);
      padding: var(--space-md);
      border: 1px solid var(--border);
      border-radius: var(--radius-card);
    }
    .head {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 6px var(--space-md);
    }
    .head b {
      font-weight: 500;
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
    .muted {
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
      overflow-wrap: anywhere;
    }
    q {
      overflow-wrap: anywhere;
    }
    .buttons {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-sm);
    }
    .form {
      display: grid;
      gap: var(--space-sm);
    }
    .row {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 220px));
      gap: var(--space-sm);
    }
    .field {
      display: grid;
      gap: 4px;
      font-size: var(--fs-sub);
      font-weight: 500;
    }
    .error {
      font-size: var(--fs-sub);
    }
    @media (max-width: 640px) {
      .row {
        grid-template-columns: minmax(0, 1fr);
      }
    }
  `,
})
export class ModelReview {
  private static next = 0;
  protected readonly id = `review-${ModelReview.next++}`;

  readonly finding = input.required<FindingView>();
  readonly members = input<readonly MemberView[]>([]);
  readonly canEdit = input(false);
  readonly busy = input(false);
  readonly review = output<ReviewChange>();

  protected readonly verdicts = VERDICTS;
  protected readonly levels = EXPECTED_LEVELS;
  protected readonly open = signal(false);
  protected readonly error = signal<string | null>(null);

  private readonly verdictField = viewChild<ElementRef<HTMLSelectElement>>('verdict');
  private readonly levelField = viewChild<ElementRef<HTMLSelectElement>>('level');
  private readonly whyField = viewChild<ElementRef<HTMLTextAreaElement>>('why');

  /** The model's verdict counted and waits for a person. */
  protected readonly estimate = computed(() => hasModelEstimate(this.finding()));
  protected readonly shown = computed(
    () => this.estimate() || (this.finding().verdictSource === 'human' && !!this.finding().review),
  );
  protected readonly proposal = computed(() => isProposal(this.finding()));
  protected readonly from = computed(() => levelWithoutModel(this.finding().factors));
  protected readonly now = computed(() => priorityOfLevel(this.finding().level));
  protected readonly held = computed(() => heldByFloor(this.finding().factors));
  private readonly transloco = inject(TranslocoService);
  /** Who reviewed it; a person who left the organization is «Someone». */
  protected readonly who = computed(() => {
    const name = reviewer(this.finding(), this.members());
    return name === 'someone' ? this.transloco.translate('actionDetail.history.someone') : name;
  });
  /** A correction of the level alone: the verdict stays the model's, the expectation is recorded. */
  protected readonly levelOnly = computed(() => {
    const r = this.finding().review;
    return r && r.decision === 'corrected' && r.verdict === null && r.expectedLevel !== null
      ? r
      : null;
  });
  /** The form starts with the verdict shown. */
  protected readonly initialVerdict = computed(() => this.finding().verdict);

  protected expectedName(level: number | string | null): string {
    return LEVEL_NAMES[num(level)] ?? LEVEL_NAMES[2];
  }

  protected toggle(): void {
    this.error.set(null);
    this.open.update((o) => !o);
    if (this.open()) queueMicrotask(() => this.verdictField()?.nativeElement.focus());
  }

  protected confirm(): void {
    this.review.emit({
      finding: this.finding(),
      decision: 'confirm',
      verdict: null,
      level: null,
      reason: null,
    });
  }

  protected submit(event: Event): void {
    event.preventDefault();
    const f = this.finding();
    const chosen = this.verdictField()?.nativeElement.value ?? f.verdict;
    const levelText = this.levelField()?.nativeElement.value ?? '';
    const level = levelText ? Number(levelText) : null;
    const reason = this.whyField()?.nativeElement.value.trim() ?? '';
    // The verdict shown again is no correction of it: then only the level is questioned.
    const verdict = chosen === f.verdict ? null : chosen;
    if (verdict === null && level === null) {
      this.error.set('estimate.errors.nothing');
      this.verdictField()?.nativeElement.focus();
      return;
    }
    if (!reason) {
      this.error.set('estimate.errors.why');
      this.whyField()?.nativeElement.focus();
      return;
    }
    this.review.emit({
      finding: f,
      decision: 'correct',
      verdict,
      level,
      reason,
    });
  }

  /** Called by the page once the review went through. */
  done(): void {
    this.open.set(false);
    this.error.set(null);
  }
}
