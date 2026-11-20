import { Component, computed, inject, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { LucideChevronRight, LucideLayers, LucideRotateCcw } from '@lucide/angular';
import { UiButton, UiKbd, UiPill } from 'ui';
import type { ActionSummary, NoticeView } from '../../core/api/client';
import { NoticeFollowUp, type FollowUpAnswer } from './notice-follow-up';
import {
  affectNote,
  cvssText,
  isExploited,
  isNoisyReason,
  reasonComponent,
  reasonPhrase,
  whatParams,
} from './notice-format';

/**
 * One notice of the inbox (master plan 17.3, 17.7; mockup notices.html; named `NoticeCard` so it is not
 * mistaken for the library's `ui-notice` callout): the source's signals without a priority of its own
 * (E23: the Exploited pill and the CVSS score as a neutral pill), the title, the reason «Your stack names
 * PostgreSQL», the three answers and the two answers of equal weight. «Affects us» opens the short
 * follow-up in place; «Doesn't affect us» takes effect at once and the page offers Undo. The card itself
 * takes the focus, so the keyboard of the page can move between cards and answer the one in focus.
 * A notice that came back once (E22) says so above its head.
 */
@Component({
  selector: 'ardea-notice-card',
  imports: [
    RouterLink,
    TranslocoPipe,
    LucideChevronRight,
    LucideLayers,
    LucideRotateCcw,
    UiButton,
    UiKbd,
    UiPill,
    NoticeFollowUp,
  ],
  template: `
    @let n = notice();
    <article
      class="card"
      tabindex="0"
      [id]="'notice-' + n.id"
      [attr.data-notice]="n.id"
      [attr.aria-labelledby]="'notice-title-' + n.id"
      [attr.aria-describedby]="'notice-why-' + n.id"
    >
      @if (n.reappearedAt) {
        <p class="back">
          <svg lucideRotateCcw aria-hidden="true"></svg>
          <span>
            <b>{{ 'notices.back.lead' | transloco }}</b>
            {{
              'notices.back.text'
                | transloco
                  : {
                      who: decider() ?? ('notices.someone' | transloco),
                      date: day(n.decidedAt),
                      reason: n.reappearedReason ?? 'exploited',
                    }
            }}
          </span>
        </p>
      }
      <header class="head">
        <div class="signals">
          @if (exploited()) {
            <ui-pill tone="alert">{{ 'notices.exploited' | transloco }}</ui-pill>
          }
          <ui-pill>
            @if (score(); as s) {
              {{ 'notices.cvss' | transloco: { score: s } }}
            } @else {
              {{ 'notices.noCvss' | transloco }}
            }
          </ui-pill>
          <span class="id">{{ n.entry.key }}</span>
        </div>
        <time class="when" [attr.datetime]="n.raisedAt">{{ day(n.raisedAt) }}</time>
        <h3 class="title" [id]="'notice-title-' + n.id">
          {{ n.entry.title ?? ('answers.happened.untitled' | transloco) }}
        </h3>
        <p class="why" [id]="'notice-why-' + n.id">
          <svg lucideLayers aria-hidden="true"></svg>
          <span>
            {{ reason().key | transloco: reason().params }}
            @if (noisy()) {
              <span class="muted"> · {{ 'notices.reason.noisy' | transloco }}</span>
            }
          </span>
        </p>
      </header>

      <div class="answers">
        <section [attr.aria-labelledby]="'notice-what-' + n.id">
          <h4 class="label" [id]="'notice-what-' + n.id">
            {{ 'notices.answers.what' | transloco }}
          </h4>
          <p class="text">{{ 'notices.answers.whatText' | transloco: what() }}</p>
        </section>
        <section [attr.aria-labelledby]="'notice-affect-' + n.id">
          <h4 class="label" [id]="'notice-affect-' + n.id">
            {{ 'notices.answers.affect' | transloco }}
          </h4>
          <p class="answer">{{ 'notices.answers.possibly' | transloco }}</p>
          <p class="note">{{ affect().key | transloco: affect().params }}</p>
        </section>
        <section [attr.aria-labelledby]="'notice-todo-' + n.id">
          <h4 class="label" [id]="'notice-todo-' + n.id">
            {{ 'notices.answers.todo' | transloco }}
          </h4>
          <p class="text">{{ 'notices.answers.todoText' | transloco: { product: product() } }}</p>
        </section>
      </div>

      @if (following()) {
        <ardea-notice-follow-up
          [notice]="n"
          [projects]="projects()"
          [reminders]="reminders()"
          [busy]="busy()"
          [problem]="problem()"
          [versionError]="versionError()"
          [projectError]="projectError()"
          (answer)="follow.emit($event)"
          (dismiss)="dismiss.emit()"
        />
      } @else {
        <footer class="actions">
          @if (canAnswer()) {
            <button
              uiButton
              type="button"
              aria-keyshortcuts="1"
              [disabled]="busy()"
              (click)="affects.emit()"
            >
              {{ 'notices.affects' | transloco }}<ui-kbd aria-hidden="true">1</ui-kbd>
            </button>
            <button
              uiButton
              type="button"
              aria-keyshortcuts="2"
              [loading]="busy()"
              (click)="notAffected.emit()"
            >
              {{ 'notices.notAffected' | transloco }}<ui-kbd aria-hidden="true">2</ui-kbd>
            </button>
          }
          <a class="more" [routerLink]="['/entries', n.entry.key]">
            {{ 'notices.details' | transloco }}<span class="sr-only"> {{ n.entry.key }}</span>
            <svg lucideChevronRight aria-hidden="true"></svg>
          </a>
        </footer>
        @if (problem(); as p) {
          <p class="problem">{{ p | transloco }}</p>
        }
      }
    </article>
  `,
  styles: `
    :host {
      display: block;
    }
    .card {
      padding: var(--card-pad);
      border: 1px solid var(--border);
      border-radius: var(--radius-card);
      background: var(--surface);
      transition: border-color var(--t-hover) ease;
    }
    .card:focus-within {
      border-color: var(--control);
    }
    .card:focus-visible {
      outline: var(--focus-ring);
      outline-offset: var(--focus-offset);
    }
    .back {
      display: flex;
      align-items: flex-start;
      gap: var(--space-sm);
      margin: calc(-1 * var(--card-pad)) calc(-1 * var(--card-pad)) var(--space-lg);
      padding: 10px var(--card-pad);
      border-bottom: 1px solid var(--rule);
      border-radius: var(--radius-card) var(--radius-card) 0 0;
      background: var(--raised);
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .back svg {
      flex: none;
      width: 16px;
      height: 16px;
      margin-top: 1px;
      color: var(--text);
    }
    .back b {
      color: var(--text);
      font-weight: 500;
    }
    .head {
      display: grid;
      grid-template-columns: minmax(0, 1fr) auto;
      align-items: center;
      gap: 6px var(--space-lg);
    }
    .signals {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 6px var(--space-sm);
      min-width: 0;
    }
    .signals .id {
      color: var(--muted);
      font-size: var(--fs-sub);
    }
    .when {
      color: var(--faint);
      font-size: var(--fs-small);
      white-space: nowrap;
    }
    .title {
      grid-column: 1 / -1;
      margin-top: var(--space-xs);
      font-size: var(--fs-section);
      line-height: 22px;
      font-weight: 600;
      overflow-wrap: anywhere;
    }
    .why {
      grid-column: 1 / -1;
      display: flex;
      align-items: flex-start;
      gap: 6px;
      color: var(--muted);
      overflow-wrap: anywhere;
    }
    .why svg {
      flex: none;
      width: 16px;
      height: 16px;
      margin-top: 2px;
      color: var(--accent);
    }
    .answers {
      display: grid;
      grid-template-columns: repeat(3, minmax(0, 1fr));
      margin: var(--space-lg) calc(-1 * var(--card-pad)) 0;
      border-top: 1px solid var(--border);
      border-bottom: 1px solid var(--border);
    }
    .answers section {
      min-width: 0;
      padding: var(--space-lg) var(--card-pad);
    }
    .answers section + section {
      border-left: 1px solid var(--border);
    }
    .label {
      margin-bottom: var(--space-xs);
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
      font-weight: 400;
    }
    .answer {
      font-size: var(--fs-section);
      line-height: 22px;
      font-weight: 500;
    }
    .text,
    .note {
      overflow-wrap: anywhere;
    }
    .note {
      margin-top: var(--space-xs);
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
    }
    .muted {
      color: var(--muted);
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-sm);
      margin-top: var(--space-lg);
    }
    .actions ui-kbd {
      margin-left: var(--space-sm);
      margin-right: -4px;
    }
    .more {
      display: inline-flex;
      align-items: center;
      gap: 2px;
      margin-left: auto;
      padding: 0 var(--space-xs);
      border-radius: var(--radius-control);
      color: var(--muted);
      font-size: var(--fs-sub);
      min-height: 32px;
    }
    .more svg {
      width: 14px;
      height: 14px;
    }
    .problem {
      margin-top: var(--space-sm);
      font-size: var(--fs-sub);
      font-weight: 500;
    }
    @media (hover: hover) and (pointer: fine) {
      .more:hover {
        color: var(--text);
      }
    }
    /* Key caps only where there is a keyboard to hint at */
    @media (hover: none), (pointer: coarse), (max-width: 640px) {
      .actions ui-kbd {
        display: none;
      }
    }
    @media (max-width: 640px) {
      .title {
        font-size: var(--fs-body);
        line-height: var(--lh-body);
      }
      .answers {
        grid-template-columns: minmax(0, 1fr);
      }
      .answers section {
        padding: var(--space-md) var(--card-pad);
      }
      .answers section + section {
        border-left: 0;
        border-top: 1px solid var(--border);
      }
      .actions button {
        flex: 1 1 0;
        min-height: 44px;
      }
      .more {
        flex: 1 1 100%;
        justify-content: center;
        margin: 0;
      }
    }
  `,
})
export class NoticeCard {
  readonly notice = input.required<NoticeView>();
  /** Members answer; viewers read (ADR 047 decision 9). */
  readonly canAnswer = input(false);
  /** The follow-up of «Affects us» is open on this card. */
  readonly following = input(false);
  readonly busy = input(false);
  /** Why the last answer was refused, as a translation key. */
  readonly problem = input<string | null>(null);
  readonly versionError = input<string | null>(null);
  readonly projectError = input<string | null>(null);
  readonly projects = input<readonly { id: string; name: string }[]>([]);
  readonly reminders = input<readonly ActionSummary[]>([]);
  /** Who answered «Doesn't affect us» before the notice came back, by name. */
  readonly decider = input<string | null>(null);

  readonly affects = output();
  readonly notAffected = output();
  readonly follow = output<FollowUpAnswer>();
  readonly dismiss = output();

  private readonly transloco = inject(TranslocoService);

  protected readonly exploited = computed(() => isExploited(this.notice()));
  protected readonly score = computed(() => cvssText(this.notice()));
  protected readonly reason = computed(() => reasonPhrase(this.notice()));
  protected readonly affect = computed(() => affectNote(this.notice()));
  protected readonly noisy = computed(() => isNoisyReason(this.notice()));
  protected readonly product = computed(() => reasonComponent(this.notice())?.name ?? '?');
  protected readonly what = computed(() => whatParams(this.notice()));

  protected day(value: string | null): string {
    if (!value) return '';
    return new Intl.DateTimeFormat(this.transloco.getActiveLang(), { dateStyle: 'medium' }).format(
      new Date(value),
    );
  }
}
