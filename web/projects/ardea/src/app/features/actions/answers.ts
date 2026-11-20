import { Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { LucideExternalLink } from '@lucide/angular';
import { UiCopyButton } from 'ui';
import {
  getStackResource,
  type AffectedItem,
  type AnswersView,
  type ModelVerdictView,
} from '../../core/api/client';
import { injectOrganization } from '../organizations/current-organization';
import { roleRank } from '../organizations/org-format';
import { num } from './action-format';
import { webUrl } from '../entries/entry-detail-format';
import { refs } from './model-format';
import {
  affectedLabel,
  checkLabel,
  quoteLang,
  rangePhrase,
  reasonLabel,
  TODO_LABELS,
} from './answers-format';

function lacksVersion(item: AffectedItem): boolean {
  return item.versionVerdict === 'unknown' && !item.component.version && !item.component.removed;
}

@Component({
  selector: 'ardea-answers',
  imports: [RouterLink, TranslocoPipe, LucideExternalLink, UiCopyButton],
  template: `
    @let a = answers();
    @let m = a.origin === 'model' ? a.model : null;
    <section class="answer-part" data-answer="happened" [attr.aria-labelledby]="uid + '-happened'">
      <h2 class="label" [id]="uid + '-happened'">
        {{ 'answers.happened.label' | transloco }}
        @if (m) {
          <span class="ai-tag">{{ 'answers.model.tag' | transloco }}</span>
        }
      </h2>
      <p class="lead">
        @if (a.whatHappened.entryKey) {
          <span class="id">{{ a.whatHappened.entryKey }}</span>
        }
        {{ a.whatHappened.title ?? ('answers.happened.untitled' | transloco) }}
      </p>
      @if (m) {
        <p class="model-text" [attr.lang]="m.language || null">{{ m.whatHappened }}</p>
        <p class="source">{{ 'answers.model.written' | transloco }}</p>
      } @else if (a.whatHappened.excerpt) {
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
      @if (a.modelNote === 'without_model') {
        <p class="source without">{{ 'answers.model.without' | transloco }}</p>
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
            @if (verdictOf(item); as v) {
              &ngsp;<span class="ai-tag">{{
                'answers.model.verdict' | transloco: { verdict: v.verdict }
              }}</span>
              @if (v.proposal) {
                &ngsp;<span class="proposal">{{ 'answers.model.proposal' | transloco }}</span>
              }
            }
            @if (versionLink(item); as to) {
              <span class="add"
                ><a class="link" [routerLink]="to.path" [queryParams]="to.query">{{
                  'answers.affected.addVersion' | transloco
                }}</a></span
              >
            }
          </li>
        }
      </ul>
      @if (m) {
        <p class="note" [attr.lang]="m.language || null">
          {{ 'answers.model.likelihood' | transloco: { likelihood: m.likelihood } }}
          {{ m.likelihoodReason }}
        </p>
      }
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
      @if (m && m.actions.length) {
        <ol class="ai-steps" [attr.lang]="m.language || null">
          @for (step of m.actions; track $index) {
            <li>
              <span
                >{{ step.text }}
                @if (step.own) {
                  <em>{{ 'answers.model.own' | transloco }}</em>
                }
                @for (n of numbers(step.sources); track n) {
                  &ngsp;<span class="ref"
                    ><span class="sr-only">{{
                      'answers.model.source' | transloco: { number: n }
                    }}</span
                    ><span aria-hidden="true">{{ n }}</span></span
                  >
                }
              </span>
            </li>
          }
        </ol>
      }
      @if (web(a.toDo.advisoryUrl); as advisory) {
        <p class="source">
          <a class="link" [href]="advisory" target="_blank" rel="noopener noreferrer">
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
    .label {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 6px;
    }
    .ai-tag {
      display: inline-flex;
      align-items: center;
      height: 18px;
      padding: 0 6px;
      border-radius: 4px;
      background: var(--accent-soft);
      color: var(--text);
      font-size: var(--fs-small);
      font-weight: 500;
      white-space: nowrap;
    }
    .proposal {
      color: var(--text);
      font-weight: 500;
    }
    .model-text {
      margin-top: var(--space-xs);
      overflow-wrap: anywhere;
    }
    .without {
      color: var(--text);
    }
    .ai-steps {
      display: grid;
      gap: 4px;
      margin-top: var(--space-xs);
      padding-left: 18px;
      list-style: decimal;
      font-size: var(--fs-sub);
      line-height: var(--lh-sub);
      overflow-wrap: anywhere;
    }
    .ai-steps em {
      margin-left: 4px;
      color: var(--muted);
      font-style: normal;
    }
    .ref {
      display: inline-grid;
      place-items: center;
      min-width: 16px;
      height: 16px;
      margin-left: 2px;
      padding: 0 3px;
      border-radius: 3px;
      background: var(--raised);
      color: var(--muted);
      font-size: 11px;
      line-height: 1;
      vertical-align: 1px;
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
    .add {
      display: block;
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
  protected readonly web = webUrl;
  readonly answers = input.required<AnswersView>();
  readonly component = input<string | null>(null);

  private readonly transloco = inject(TranslocoService);
  protected readonly lang = computed(() => quoteLang(this.transloco.getActiveLang()));
  protected readonly verdict = computed(() => affectedLabel(this.answers().affected.verdict));
  protected readonly todoLabel = computed(
    () => TODO_LABELS[this.answers().toDo.kind] ?? 'answers.todo.noFix',
  );
  protected readonly moreEntries = computed(() => num(this.answers().whatHappened.moreEntries));
  protected readonly moreItems = computed(() => num(this.answers().affected.moreItems));
  protected readonly numbers = refs;
  private readonly organization = injectOrganization().organization;
  private readonly versionMissing = computed(
    () =>
      roleRank(this.organization()?.role) >= roleRank('member') &&
      this.answers().affected.items.some(lacksVersion),
  );
  private readonly stack = getStackResource(
    computed(() => (this.versionMissing() ? this.organization()?.id : undefined)),
  );
  private readonly verdicts = computed(() => {
    const a = this.answers();
    return new Map(
      (a.origin === 'model' ? (a.model?.verdicts ?? []) : []).map((v) => [v.component.id, v]),
    );
  });

  protected versionLink(
    item: AffectedItem,
  ): { path: string[]; query: Record<string, string> | null } | null {
    if (!this.versionMissing() || !lacksVersion(item) || !this.stack.hasValue()) return null;
    const org = ['/o', this.organization()?.slug ?? ''];
    return this.stack.value()?.projectId === item.project.id
      ? { path: [...org, 'stack'], query: null }
      : { path: [...org, 'projects', item.project.id, 'setup'], query: { step: 'technologies' } };
  }

  protected verdictOf(item: AffectedItem): ModelVerdictView | null {
    return this.verdicts().get(item.component.id) ?? null;
  }

  protected checkLine(item: AffectedItem): string {
    const t = (key: string, params?: Record<string, string>) =>
      this.transloco.translate(key, params ?? {});
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
