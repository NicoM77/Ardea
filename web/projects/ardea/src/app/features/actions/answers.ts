import { Component, computed, inject, input } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { LucideExternalLink } from '@lucide/angular';
import { UiCopyButton } from 'ui';
import type { AffectedItem, AnswersView } from '../../core/api/client';
import { num } from './action-format';
import {
  affectedLabel,
  checkLabel,
  quoteLang,
  rangePhrase,
  reasonLabel,
  TODO_LABELS,
} from './answers-format';

/**
 * The three answers that open an action and an alert (master plan 11.2, «in five seconds»): what
 * happened, are we affected, what to do. Built from the structured answers of the API (task 3.6);
 * the source's own words are a quotation with their source named. At 390 × 844 all three sit above
 * the action bar, so the layout stays compact on phones.
 */
@Component({
  selector: 'ardea-answers',
  imports: [TranslocoPipe, LucideExternalLink, UiCopyButton],
  template: `
    @let a = answers();
    <section class="answer-part" data-answer="happened" [attr.aria-labelledby]="uid + '-happened'">
      <h2 class="label" [id]="uid + '-happened'">{{ 'answers.happened.label' | transloco }}</h2>
      <p class="lead">
        @if (a.whatHappened.entryKey) {
          <span class="id">{{ a.whatHappened.entryKey }}</span>
        }
        {{ a.whatHappened.title ?? ('answers.happened.untitled' | transloco) }}
      </p>
      @if (a.whatHappened.excerpt) {
        <figure>
          <blockquote [attr.lang]="lang()">{{ a.whatHappened.excerpt }}</blockquote>
          @if (a.whatHappened.source) {
            <figcaption class="source">
              {{ 'answers.happened.source' | transloco: { source: a.whatHappened.source } }}
            </figcaption>
          }
        </figure>
      }
      @if (moreEntries()) {
        <p class="source">{{ 'answers.happened.more' | transloco: { count: moreEntries() } }}</p>
      }
    </section>

    <section class="answer-part" data-answer="affected" [attr.aria-labelledby]="uid + '-affected'">
      <h2 class="label" [id]="uid + '-affected'">{{ 'answers.affected.label' | transloco }}</h2>
      <p class="answer">{{ verdict() | transloco }}</p>
      <ul class="items">
        @for (item of a.affected.items; track item.component.id) {
          <li>
            <b>{{ item.project.name }}</b> · {{ item.component.name }}
            @if (item.component.version) {
              <span class="id">{{ item.component.version }}</span>
            }
            <span class="check">{{ checkLine(item) }}</span>
          </li>
        }
      </ul>
      @if (moreItems()) {
        <p class="source">{{ 'answers.affected.more' | transloco: { count: moreItems() } }}</p>
      }
    </section>

    <section class="answer-part" data-answer="todo" [attr.aria-labelledby]="uid + '-todo'">
      <h2 class="label" [id]="uid + '-todo'">{{ 'answers.todo.label' | transloco }}</h2>
      @switch (a.toDo.kind) {
        @case ('update') {
          <p class="answer">
            {{ 'answers.todo.updateTo' | transloco: { version: a.toDo.targetVersion } }}
          </p>
          <p class="step">
            <code>{{ component() ? component() + ' ' : '' }}{{ a.toDo.targetVersion }}</code>
            <ui-copy-button
              variant="ghost"
              [text]="a.toDo.targetVersion ?? ''"
              [label]="'answers.todo.copy' | transloco"
            />
          </p>
          @if (a.toDo.installedVersion) {
            <p class="source">
              {{ 'answers.todo.from' | transloco: { version: a.toDo.installedVersion } }}
            </p>
          }
        }
        @case ('source_guidance') {
          <p class="answer">{{ todoLabel() | transloco }}</p>
          <ol class="steps" [attr.lang]="lang()">
            @for (step of a.toDo.steps; track $index) {
              <li>
                <q>{{ step }}</q>
              </li>
            }
          </ol>
        }
        @default {
          <p class="answer">{{ todoLabel() | transloco }}</p>
          <p class="note">{{ 'answers.todo.noFixNote' | transloco }}</p>
        }
      }
      @if (a.toDo.advisoryUrl) {
        <p class="source">
          <a class="link" [href]="a.toDo.advisoryUrl" target="_blank" rel="noopener noreferrer">
            {{ 'answers.todo.advisory' | transloco }}
            <svg lucideExternalLink aria-hidden="true"></svg>
          </a>
        </p>
      }
    </section>
  `,
  styles: `
    :host {
      display: grid;
      grid-template-columns: repeat(3, minmax(0, 1fr));
    }
    .answer-part {
      min-width: 0;
      padding: var(--card-pad);
    }
    .answer-part + .answer-part {
      border-left: 1px solid var(--border);
    }
    .label {
      margin-bottom: var(--space-xs);
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
      font-weight: 400;
    }
    .lead {
      font-weight: 500;
      overflow-wrap: anywhere;
    }
    .lead .id {
      margin-right: var(--space-xs);
    }
    .answer {
      font-size: var(--fs-section);
      line-height: 22px;
      font-weight: 500;
      overflow-wrap: anywhere;
    }
    figure {
      margin: var(--space-xs) 0 0;
    }
    blockquote,
    .items,
    .note,
    .steps {
      margin: var(--space-xs) 0 0;
      color: var(--muted);
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
      overflow-wrap: anywhere;
    }
    blockquote {
      padding-left: var(--space-sm);
      border-left: 2px solid var(--rule);
      color: var(--text);
    }
    .items li + li {
      margin-top: 2px;
    }
    .items b {
      color: var(--text);
      font-weight: 500;
    }
    .steps {
      display: grid;
      gap: 2px;
      padding-left: 18px;
      list-style: decimal;
    }
    .source {
      margin-top: var(--space-xs);
      color: var(--muted);
      font-size: var(--fs-small);
      line-height: var(--lh-small);
    }
    .source .link {
      display: inline-flex;
      align-items: center;
      gap: 4px;
    }
    .source svg {
      width: 12px;
      height: 12px;
    }
    .step {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-xs) var(--space-sm);
      margin-top: var(--space-xs);
    }
    code {
      display: inline-flex;
      align-items: center;
      min-height: 28px;
      padding: 0 var(--space-sm);
      border-radius: var(--radius-control);
      background: var(--raised);
      font: 500 var(--fs-sub) / 1.3 var(--mono);
      overflow-wrap: anywhere;
    }
    @media (max-width: 1100px) and (min-width: 641px) {
      .answer-part {
        padding: var(--space-lg);
      }
    }
    @media (max-width: 640px) {
      :host {
        grid-template-columns: minmax(0, 1fr);
      }
      .answer-part {
        padding: var(--space-md) var(--card-pad);
      }
      .answer-part + .answer-part {
        border-left: 0;
        border-top: 1px solid var(--border);
      }
    }
  `,
})
export class Answers {
  private static next = 0;
  protected readonly uid = `answers-${Answers.next++}`;
  readonly answers = input.required<AnswersView>();
  /** The component the target version belongs to, written before it («PostgreSQL 17.12»). */
  readonly component = input<string | null>(null);

  private readonly transloco = inject(TranslocoService);
  protected readonly lang = computed(() => quoteLang(this.transloco.getActiveLang()));
  protected readonly verdict = computed(() => affectedLabel(this.answers().affected.verdict));
  protected readonly todoLabel = computed(
    () => TODO_LABELS[this.answers().toDo.kind] ?? 'answers.todo.noFix',
  );
  protected readonly moreEntries = computed(() => num(this.answers().whatHappened.moreEntries));
  protected readonly moreItems = computed(() => num(this.answers().affected.moreItems));

  /**
   * One component's check in one line: «In range: 17 to before 17.12», «Version unknown (no
   * version recorded)». Put together here, so the punctuation sits right in every language.
   */
  protected checkLine(item: AffectedItem): string {
    const t = (key: string, params?: Record<string, string>) =>
      this.transloco.translate(key, params ?? {});
    // The reason is worth a word only where the version could not be checked.
    const why = item.versionVerdict === 'unknown' ? reasonLabel(item.versionReason) : null;
    const ranges = item.ranges.map((range) => {
      const phrase = rangePhrase(range);
      return t(phrase.key, phrase.params);
    });
    return (
      '\u00a0· ' +
      t(checkLabel(item.versionVerdict)) +
      (why ? ` (${t(why)})` : '') +
      (ranges.length ? `: ${ranges.join('; ')}` : '')
    );
  }
}
